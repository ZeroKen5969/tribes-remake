using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="BuildingExit"/> with the original, plus edge cases.</summary>
public sealed class BuildingExitTests
{
    private const int TargetField = 44, SubStateField = 13;

    private readonly ITestOutputHelper output;

    public BuildingExitTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Leave_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var inside = Enumerable.Range(1, 100).Where(id => LegacySamples.Field(game, UnitSampling.HitPointsField, id) > 0
                && !UnitSampling.OnItsCell(game, id) && LegacySamples.Field(game, SubStateField, id) == -3
                && LegacySamples.Field(game, TargetField, id) is >= 1 and <= 60);
            foreach (int id in inside)
            {
                int goal = UnitSampling.GoalDistance(game, id);
                run.Check(sample, $"Leave unit {id}",
                    g => { Legacy.World w = UnitSampling.Turn(g, id, goal); w.x_(); return w.i_z; },
                    s =>
                    {
                        UnitTurn turn = s.UnitGoals.StartTurn(s.World.Unit(id));
                        s.BuildingExit.Leave(turn);
                        return turn.GoalDistance;
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 30);
    }

    [Fact]
    public void Leave_PutsTheUnitOnTheFreeCellNearestTheExit()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Building hut = world.Building(4);
        (hut.Kind, hut.HitPoints, hut.Level, hut.X, hut.Y, hut.OccupantCount) = (BuildingKindNumbers.Hut, 20, 1, 10, 10, 1);
        Unit unit = world.Unit(5);
        (unit.HitPoints, unit.Age, unit.TargetIndex, unit.ActionSubState, unit.Action, unit.ThrowRange) = (10, 30, 4, -3, 87, 9);
        hut.FirstOccupant = 5;

        systems.BuildingExit.Leave(systems.UnitGoals.StartTurn(unit));

        Assert.Equal((0, 0, -1, 7), (hut.FirstOccupant, hut.OccupantCount, unit.BuildingSlot, unit.ThrowRange));
        Assert.Equal(MapCell.UnitAt(5), world.Map.CellAt(new GridPoint(unit.X, unit.Y)));
    }

    [Fact]
    public void Leave_AScoutLeavingInTheTutorialGetsItsScene()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        world.Script.TutorialFlags = 1;
        Unit scout = Occupant(world, 6, 9);
        scout.ScoutingSkill = 5;

        systems.BuildingExit.Leave(systems.UnitGoals.StartTurn(scout));

        Assert.Equal(1 | 2, world.Script.TutorialFlags);
        Assert.Equal((true, 61), (world.Script.Active, world.Script.SceneId));
    }

    [Fact]
    public void Leave_AVisitorWithoutNeedsWandersOffAndASickOneTakesTheSignAlong()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit visitor = Occupant(world, 7, 10);
        (visitor.Action, visitor.Status) = (ActionNumbers.StayInside, UnitStatus.Diseased);
        world.Building(10).Flags = 0x02;
        Unit other = world.Unit(8);
        (other.HitPoints, other.Status, other.NextOccupantOrCellObject) = (10, UnitStatus.Diseased, 0);
        visitor.NextOccupantOrCellObject = 8;

        UnitTurn turn = systems.UnitGoals.StartTurn(visitor);
        systems.BuildingExit.Leave(turn);

        Assert.Equal((ActionNumbers.Idle, visitor.X, visitor.Y), (visitor.AssignedTask, visitor.TaskSiteX, visitor.TaskSiteY));
        Assert.Equal(0, world.Building(10).Flags & 0x02);
        Assert.Contains(systems.PendingEvents, e => e is UnitDeselected { Unit: 7 });
    }

    private static Unit Occupant(GameWorld world, int id, int building)
    {
        Building hut = world.Building(building);
        (hut.Kind, hut.HitPoints, hut.Level, hut.X, hut.Y, hut.OccupantCount, hut.FirstOccupant) = (BuildingKindNumbers.Hut, 20, 1, 12, 12, 1, id);
        Unit unit = world.Unit(id);
        (unit.HitPoints, unit.Age, unit.TargetIndex, unit.ActionSubState, unit.Action, unit.ThrowRange) = (10, 30, building, -3, 87, 9);
        return unit;
    }
}
