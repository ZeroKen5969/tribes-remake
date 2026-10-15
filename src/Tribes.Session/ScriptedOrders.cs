using System;
using System.Collections.Generic;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.World;

namespace Tribes.Session;

/// <summary>
/// Random but well-formed orders for one tribe, made from the world the player sees: moves of
/// its living units, buildings from the build menu, calls to arms, building work, needs and hint
/// switches. Used by automated matches (tests, headless instances); the rules may still refuse
/// an order, as they would a person's.
/// </summary>
public sealed class ScriptedOrders
{
    private const int Reach = 8;
    private readonly Random random;

    /// <summary>Creates a script; the same seed and worlds give the same orders.</summary>
    public ScriptedOrders(int seed)
    {
#pragma warning disable CA5394 // Not security relevant: reproducible test input.
        random = new Random(seed);
#pragma warning restore CA5394
    }

    /// <summary>An order for <paramref name="tribe"/>, or null when it has nothing to order.</summary>
    public GameOrder? Next(GameSimulation simulation, int tribe)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        GameWorld world = simulation.World;
        if (tribe < 0 || tribe >= world.Tribes.Count)
        {
            return null;
        }
        List<int> units = LivingUnits(world, tribe);
        List<int> buildings = StandingBuildings(world, tribe);
        if (units.Count == 0)
        {
            return buildings.Count == 0 ? null : new SetHints(tribe, Roll(2) == 0);
        }
        Unit anchor = world.Unit(units[Roll(units.Count)]);
        GridPoint near = Near(world, anchor.X, anchor.Y);
        int pick = Roll(100);
        return pick switch
        {
            < 45 => new MoveUnits(tribe, PickUnits(units), near, 0, Roll(4) == 0),
            < 60 => PlaceBuilding(world, tribe, near),
            < 68 => new CallArmy(tribe, Roll(2) == 0 ? ArmyCall.Rally : ArmyCall.AllOut, near),
            < 74 => new CancelArmyCall(tribe, Roll(2) == 0 ? ArmyCall.Rally : ArmyCall.AllOut),
            < 84 when buildings.Count > 0 => new OrderBuildingWork(tribe, buildings[Roll(buildings.Count)],
                Roll(2) == 0 ? BuildingWork.Upgrade : BuildingWork.Repair),
            < 92 => new TendNeed(tribe, anchor.Id, Roll(2) == 0 ? UnitNeed.Food : UnitNeed.Rest),
            < 96 => new SetHints(tribe, Roll(2) == 0),
            _ => new CallOutUnit(tribe, anchor.Id),
        };
    }

    /// <summary>Bytes that are not an order of the codec (for tests of how peers ignore them).</summary>
    public byte[] Garbage()
    {
        byte[] bytes = new byte[1 + Roll(12)];
        random.NextBytes(bytes);
        // A wrong format version makes sure the codec refuses it.
        bytes[0] = (byte)(GameOrderCodec.FormatVersion + 1 + Roll(200));
        return bytes;
    }

    private int Roll(int count) => random.Next(count);

    private UnitSet PickUnits(List<int> units)
    {
        int count = Math.Min(units.Count, 1 + Roll(3));
        var picked = new List<int>(count);
        while (picked.Count < count)
        {
            int unit = units[Roll(units.Count)];
            if (!picked.Contains(unit))
            {
                picked.Add(unit);
            }
        }
        return new UnitSet(picked);
    }

    private PlaceBuilding PlaceBuilding(GameWorld world, int tribe, GridPoint near)
    {
        IReadOnlyList<int> kinds = OrderMenus.BuildableKinds(world);
        int kind = kinds.Count > 0 ? kinds[Roll(kinds.Count)] : 0;
        return new PlaceBuilding(tribe, kind, near);
    }

    private GridPoint Near(GameWorld world, int x, int y)
    {
        GridSize size = world.Map.PlayableSize;
        int nx = Math.Clamp(x + Roll((2 * Reach) + 1) - Reach, 0, size.Width - 1);
        int ny = Math.Clamp(y + Roll((2 * Reach) + 1) - Reach, 0, size.Height - 1);
        return new GridPoint(nx, ny);
    }

    private static List<int> LivingUnits(GameWorld world, int tribe)
    {
        var units = new List<int>();
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            if (unit.InUse)
            {
                units.Add(unit.Id);
            }
        }
        return units;
    }

    private static List<int> StandingBuildings(GameWorld world, int tribe)
    {
        var buildings = new List<int>();
        foreach (Building building in world.BuildingsOf(tribe))
        {
            if (building.Stands)
            {
                buildings.Add(building.Id);
            }
        }
        return buildings;
    }
}
