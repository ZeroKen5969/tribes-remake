using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Tests.Systems;
using Tribes.Core.World;

namespace Tribes.Core.Tests.Commands;

/// <summary>A small world for order tests: an empty map the player sees all of, with helpers to put units and buildings on it.</summary>
internal sealed class OrderWorld
{
    public const int Player = 0, Computer = 1;

    public OrderWorld(int mission = 2)
    {
        Systems = new RuleSystems(TestWorlds.Empty());
        World.Mission = mission;
        for (int y = 0; y < World.Map.Size.Height; y++)
        {
            for (int x = 0; x < World.Map.Size.Width; x++)
            {
                World.Fog(Player).SetExplored(new GridPoint(x, y), true);
                World.Fog(Player).SetVisible(new GridPoint(x, y), true);
            }
        }
    }

    public RuleSystems Systems { get; }

    public GameWorld World => Systems.World;

    public OrderOutcome Apply(GameOrder order) => Systems.ApplyOrder(order);

    /// <summary>A living, happy adult in unit slot <paramref name="id"/> standing on its cell.</summary>
    public Unit Unit(int id, int x, int y)
    {
        Unit unit = World.Unit(id);
        unit.HitPoints = 20;
        unit.MaxHitPoints = 20;
        unit.Age = 20;
        unit.Happiness = 50;
        unit.MoveDelay = 5;
        unit.Weapon = WeaponNumbers.BareHands;
        unit.Action = ActionNumbers.Idle;
        (unit.X, unit.Y, unit.DestinationX, unit.DestinationY) = (x, y, x, y);
        World.Map.SetCell(new GridPoint(x, y), MapCell.UnitAt(id));
        return unit;
    }

    /// <summary>A standing building of <paramref name="kind"/> placed at once for <paramref name="tribe"/>.</summary>
    public Building Building(int kind, int x, int y, int tribe = Player, int level = 1)
    {
        PlacementResult placed = Systems.BuildingPlacement.Place(new PlacementRequest(tribe, kind, new GridPoint(x, y), level, Immediate: true));
        return World.Building(placed.Slot);
    }
}
