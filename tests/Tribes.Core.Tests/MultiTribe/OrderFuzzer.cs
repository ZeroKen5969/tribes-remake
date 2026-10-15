using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.World;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// Orders of every kind as a careless or malicious client might send them: for any tribe (also
/// tribes that do not exist), with cells near the units, on and just off the map's edges, far off
/// it and at the ends of the int range, with slots of other tribes, free slots and impossible
/// ones, out-of-range kinds, products and choices, and as encoded payloads, damaged payloads and
/// plain garbage. The same seed and worlds give the same orders.
/// </summary>
internal sealed class OrderFuzzer
{
    private const int OrderKinds = 23, NearReach = 10, EdgeBand = 2, OffMapReach = 6, SmallRange = 40;

    private static readonly int[] ExtremeValues = [int.MinValue, int.MinValue + 1, -1_000_000, -256, -129, -128, -2, -1, 0,
        127, 128, 255, 256, 32767, 65536, 1 << 20, int.MaxValue - 1, int.MaxValue];

    private readonly Random random;
    private GameWorld world = null!;

    public OrderFuzzer(int seed)
    {
#pragma warning disable CA5394 // Not security relevant: reproducible test input.
        random = new Random(seed);
#pragma warning restore CA5394
    }

    /// <summary>Between none and a few orders for every tribe (and now and then for no tribe of the world).</summary>
    public TickInput Tick(GameSimulation game)
    {
        world = game.World;
        var orders = new List<PlayerOrder>();
        for (int tribe = 0; tribe < world.Tribes.Count; tribe++)
        {
            int count = Roll(4) == 0 ? 0 : 1 + Roll(3);
            for (int i = 0; i < count; i++)
            {
                orders.Add(Next(tribe));
            }
        }
        if (Roll(10) == 0)
        {
            orders.Add(PlayerOrder.From(Order(Pick(-1, world.Tribes.Count, world.Tribes.Count + 7, int.MinValue, int.MaxValue))));
        }
        return new TickInput { Orders = orders };
    }

    private PlayerOrder Next(int tribe)
    {
        int form = Roll(20);
        if (form == 0)
        {
            return PlayerOrder.FromPayload(tribe, Garbage());
        }
        int orderTribe = Roll(12) == 0 ? AnyTribe() : tribe;
        if (form <= 6)
        {
            // Over the wire a choice is one byte; values beyond those come as damaged payloads.
            byte[] payload = GameOrderCodec.Encode(Order(orderTribe, wireChoices: true));
            if (form == 1)
            {
                payload[Roll(payload.Length)] ^= (byte)(1 + Roll(255));
            }
            return PlayerOrder.FromPayload(tribe, payload);
        }
        return PlayerOrder.From(Order(orderTribe, wireChoices: false));
    }

    private GameOrder Order(int tribe, bool wireChoices = false) => Roll(OrderKinds) switch
    {
        0 => new MoveUnits(tribe, Units(tribe), Cell(tribe), TargetCode(), Roll(3) == 0),
        1 => new PlaceBuilding(tribe, Kind(), Cell(tribe)),
        2 => new PlaceTrap(tribe, Building(tribe), Roll(2) == 0 ? Small() : 20 + Roll(4), Cell(tribe)),
        3 => new DemolishBuilding(tribe, Building(tribe)),
        4 => new OrderBuildingWork(tribe, Building(tribe), Choice<BuildingWork>(4, wireChoices)),
        5 => new EvictUnit(tribe, Building(tribe), Unit(tribe)),
        6 => new CallOutUnit(tribe, Unit(tribe)),
        7 => new AppointChief(tribe, Unit(tribe)),
        8 => new CallArmy(tribe, Choice<ArmyCall>(2, wireChoices), Cell(tribe)),
        9 => new CancelArmyCall(tribe, Choice<ArmyCall>(2, wireChoices)),
        10 => new SetArmouryOrder(tribe, Building(tribe), Small(), Small()),
        11 => new FetchWeapon(tribe, Units(tribe), Building(tribe), Small(), StoreCell(tribe)),
        12 => new DeliverToStore(tribe, Units(tribe), Building(tribe), StoreCell(tribe)),
        13 => new CastSpell(tribe, Building(tribe), Small(), Unit(tribe)),
        14 => new CastSpellAt(tribe, Building(tribe), Small(), Cell(tribe)),
        15 => new TendNeed(tribe, Unit(tribe), Choice<UnitNeed>(2, wireChoices)),
        16 => new SetHints(tribe, Roll(2) == 0),
        17 => new ViewedHelp(tribe),
        18 => new InviteAlly(tribe, AnyTribe()),
        19 => new AnswerAlliance(tribe, AnyTribe(), Roll(2) == 0),
        20 => new LeaveAlliance(tribe, AnyTribe()),
        21 => new TransferGoods(tribe, AnyTribe(), Choice<TribeCounter>(TribeState.CounterCount, wireChoices), Small()),
        _ => new AdvanceScene(tribe),
    };

    private int Roll(int count) => random.Next(count);

    private int Pick(params int[] values) => values[Roll(values.Length)];

    private int Extreme() => ExtremeValues[Roll(ExtremeValues.Length)];

    private int AnyTribe() => Roll(3) == 0 ? Extreme() : Roll(world.Tribes.Count + 2) - 1;

    private int Small() => Roll(5) == 0 ? Extreme() : Roll(SmallRange) - 3;

    private T Choice<T>(int count, bool wire) where T : struct, Enum =>
        (T)(object)(wire ? Roll(count + 3) : Roll(5) == 0 ? Extreme() : Roll(count + 2) - 1);

    private int Kind()
    {
        IReadOnlyList<int> kinds = OrderMenus.BuildableKinds(world);
        return Roll(4) != 0 && kinds.Count > 0 ? kinds[Roll(kinds.Count)] : Small();
    }

    private List<int> LivingUnits(int tribe)
    {
        var units = new List<int>();
        if (tribe < 0 || tribe >= world.Tribes.Count)
        {
            return units;
        }
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            if (unit.HitPoints > 0)
            {
                units.Add(unit.Id);
            }
        }
        return units;
    }

    private List<int> StandingBuildings(int tribe)
    {
        var buildings = new List<int>();
        if (tribe < 0 || tribe >= world.Tribes.Count)
        {
            return buildings;
        }
        foreach (Building building in world.BuildingsOf(tribe))
        {
            if (building.Stands)
            {
                buildings.Add(building.Id);
            }
        }
        return buildings;
    }

    private int Unit(int tribe)
    {
        List<int> own = LivingUnits(tribe);
        return Roll(10) switch
        {
            < 5 when own.Count > 0 => own[Roll(own.Count)],
            < 8 => 1 + Roll(world.UnitSlots),
            _ => Pick(0, -1, world.UnitSlots + 1, world.UnitSlots + 300, Extreme()),
        };
    }

    private int Building(int tribe)
    {
        List<int> own = StandingBuildings(tribe);
        return Roll(10) switch
        {
            < 5 when own.Count > 0 => own[Roll(own.Count)],
            < 8 => 1 + Roll(world.BuildingSlots),
            _ => Pick(0, -1, world.BuildingSlots + 1, Extreme()),
        };
    }

    private UnitSet Units(int tribe)
    {
        List<int> own = LivingUnits(tribe);
        int count = Roll(8) == 0 ? 0 : 1 + Roll(5);
        var picked = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int unit = Roll(5) != 0 && own.Count > 0 ? own[Roll(own.Count)] : Unit(tribe);
            if (!picked.Contains(unit))
            {
                picked.Add(unit);
            }
        }
        return new UnitSet(picked);
    }

    private int TargetCode() => Roll(6) switch
    {
        0 => CellCode.OfUnit(Pick(1 + Roll(world.UnitSlots), 0, -1, world.UnitSlots + 1, Extreme())),
        1 => CellCode.OfBuilding(Pick(1 + Roll(world.BuildingSlots), 0, world.BuildingSlots + 1, Extreme())),
        2 => CellCode.OfAnimal(Pick(Roll(Math.Max(1, world.AnimalSlots)), -1, world.AnimalSlots, Extreme())),
        3 => 0,
        _ => Extreme(),
    };

    private GridPoint Cell(int tribe)
    {
        GridSize size = world.Map.PlayableSize;
        List<int> own = LivingUnits(tribe);
        switch (Roll(10))
        {
            case < 3 when own.Count > 0:
                Unit near = world.Unit(own[Roll(own.Count)]);
                return new GridPoint(near.X + Roll((2 * NearReach) + 1) - NearReach, near.Y + Roll((2 * NearReach) + 1) - NearReach);
            case < 6:
                return Edge(size);
            case < 8:
                return new GridPoint(Roll(size.Width + (2 * OffMapReach)) - OffMapReach, Roll(size.Height + (2 * OffMapReach)) - OffMapReach);
            default:
                return new GridPoint(Roll(2) == 0 ? Extreme() : Roll(size.Width), Roll(2) == 0 ? Extreme() : Roll(size.Height));
        }
    }

    /// <summary>A cell in the band just inside or just outside one of the map's edges.</summary>
    private GridPoint Edge(GridSize size)
    {
        int across = Roll((2 * EdgeBand) + 1) - EdgeBand;
        return Roll(4) switch
        {
            0 => new GridPoint(across, Roll(size.Height)),
            1 => new GridPoint(size.Width - 1 - across, Roll(size.Height)),
            2 => new GridPoint(Roll(size.Width), across),
            _ => new GridPoint(Roll(size.Width), size.Height - 1 - across),
        };
    }

    /// <summary>Mostly a cell of (or next to) a building of the tribe, else any cell.</summary>
    private GridPoint StoreCell(int tribe)
    {
        List<int> own = StandingBuildings(tribe);
        if (Roll(3) == 0 || own.Count == 0)
        {
            return Cell(tribe);
        }
        Building building = world.Building(own[Roll(own.Count)]);
        return new GridPoint(building.X + Roll(6) - 1, building.Y + Roll(6) - 1);
    }

    private byte[] Garbage()
    {
        byte[] bytes = new byte[Roll(24)];
        random.NextBytes(bytes);
        if (bytes.Length > 0 && Roll(2) == 0)
        {
            bytes[0] = GameOrderCodec.FormatVersion;
        }
        return bytes;
    }
}
