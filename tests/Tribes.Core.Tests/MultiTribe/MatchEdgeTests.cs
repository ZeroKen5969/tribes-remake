using System;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>Corner cases of matches: no room, allies in cells, last sides, odd standings.</summary>
public sealed class MatchEdgeTests
{
    [Fact]
    public void TooManyTribes_ForTheMap_IsRefused()
    {
        MatchSetup setup = MatchWorlds.Setup(4, 1, MatchWorlds.Controllers(0, 60));
        Assert.Throws<ArgumentException>(() => GameSimulation.StartMatch(MatchWorlds.Content, setup));
    }

    [Fact]
    public void AnAlly_ReadsAsSomethingInTheWay_AndAnimalsSeeThroughFog()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 2, MatchWorlds.Controllers(1, 2), [1, 1, 2]));
        GameWorld world = game.World;
        var cells = new CellClassifier(world);
        Unit ally = world.Unit(world.FirstUnitSlot(1));
        Unit enemy = world.Unit(world.FirstUnitSlot(2));
        CellProbe allyProbe = cells.Probe(CellCode.OfUnit(ally.Id), new GridPoint(ally.X, ally.Y), 2);
        Assert.Equal(CellCategories.Unit, allyProbe.Category);
        Assert.True(allyProbe.OtherSide);
        Unit human = world.Unit(world.FirstUnitSlot(0));
        CellProbe mate = cells.Probe(CellCode.OfUnit(human.Id), new GridPoint(human.X, human.Y), 1);
        Assert.Equal(CellCategories.Other, mate.Category);
        Assert.False(mate.OtherSide);
        CellProbe hidden = cells.Probe(CellCode.OfUnit(enemy.Id), new GridPoint(enemy.X, enemy.Y), 0);
        Assert.Equal(CellCategories.Empty, hidden.Category);
        CellProbe seen = cells.Probe(CellCode.OfUnit(enemy.Id), new GridPoint(enemy.X, enemy.Y), CellClassifier.NoViewer);
        Assert.Equal((CellCategories.Unit, enemy.Id), (seen.Category, seen.Slot));
        CellProbe building = cells.Probe(CellCode.OfBuilding(world.FirstBuildingSlot(2)), new GridPoint(enemy.X, enemy.Y), 1);
        Assert.Equal((CellCategories.Building, world.FirstBuildingSlot(2)), (building.Category, building.Slot));
        CellProbe node = cells.Probe(CellCode.OfNode(0), new GridPoint(1, 1), CellClassifier.NoViewer);
        Assert.Equal(CellCategories.ResourceNode, node.Category);
    }

    [Fact]
    public void Standing_WithWinnersOfNoCommonTeam_HasNoWinningTeam()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 3, MatchWorlds.Controllers(3, 0), [-1, 4, 5]));
        game.World.Relations.SetAlliance(1, 2, true);
        game.SetController(0, TribeController.Forfeited(0));
        game.SetController(0, TribeController.Forfeited(0));
        MatchResult result = game.Match;
        Assert.True(result.IsDecided);
        Assert.Equal(new[] { 1, 2 }, result.Winners);
        Assert.Equal(-1, result.WinningTeam);
        Assert.Equal(TribeOutcome.Won, result.Tribes[1].Outcome);
        Assert.Equal(result.Tribes[1].Frame, game.World.Match.DecidedAtFrame);
        Assert.Throws<InvalidOperationException>(() => game.SetController(0, TribeController.Absent(0)));
        Assert.False(MatchStandings.IsDefeated(game.World, 9));
    }

    [Fact]
    public void EndlessMatch_GoesOnWithOneSideLeft()
    {
        MatchSetup setup = MatchWorlds.Setup(2, 3, MatchWorlds.Controllers(2, 0)) with { EndsWithLastSide = false };
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, setup);
        game.SetController(1, TribeController.Forfeited(1));
        game.Step();
        Assert.False(game.Match.IsDecided);
        Assert.Equal(TribeOutcome.Playing, game.Match.Tribes[0].Outcome);
        game.SetController(0, TribeController.Absent(0));
        game.SetController(0, TribeController.Human(2));
        Assert.Equal(2, game.World.Tribes[0].Controller.Slot);
    }

    [Fact]
    public void ComputerWithNoEnemyLeft_SendsNoWave()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 7, MatchWorlds.Controllers(0, 2), [3, 3]));
        for (int tick = 0; tick < 6000; tick++)
        {
            game.Step();
        }
        Assert.All(game.World.Tribes, t => Assert.Equal(-1, t.Ai.WaveTribe));
        Assert.All(game.World.Tribes, t => Assert.Empty(t.Ai.Route));
        Assert.True(game.Match.IsDecided);
    }

    [Fact]
    public void Seat_FallsBackToAnAdult_ThenTheCamp()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(3, 5, MatchWorlds.Controllers(1, 1)));
        GameWorld world = game.World;
        foreach (Building building in world.BuildingsOf(1))
        {
            building.HitPoints = 0;
        }
        Unit adult = world.Unit(MatchWorlds.LiveAdults(world, 1).First());
        Assert.Equal(new GridPoint(adult.X, adult.Y), TribeStanding.Seat(world, 1));
        Assert.Equal(MatchWorlds.LiveAdults(world, 1).Count(), TribeStanding.LivingAdults(world, 1));
        Assert.Null(TribeStanding.FirstStandingBuilding(world, 1));
    }

    [Fact]
    public void KillsInAMatch_AreCreditedToTheEnemyThatDealtThem()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 9, MatchWorlds.Controllers(0, 3), [0, 0, 1]));
        GameWorld world = game.World;
        Unit victim = world.Unit(MatchWorlds.LiveAdults(world, 0).First());
        victim.HitPoints = 0;
        game.Rules.Deaths.Die(EntityProperty.UnitHitPoints, new GridPoint(victim.X, victim.Y), victim.Id, false, killer: 1);
        Unit second = world.Unit(MatchWorlds.LiveAdults(world, 0).First());
        second.HitPoints = 0;
        game.Rules.Deaths.Die(EntityProperty.UnitHitPoints, new GridPoint(second.X, second.Y), second.Id, false, killer: 2);
        game.Rules.BuildingLifecycle.Destroy(world.Building(world.FirstBuildingSlot(0)), 2);
        Assert.Equal(0, world.Tribes[1][TribeCounter.UnitsKilled]);
        Assert.Equal(1, world.Tribes[2][TribeCounter.UnitsKilled]);
        Assert.Equal(1, world.Tribes[2][TribeCounter.BuildingsDestroyed]);
        Assert.Contains(game.Rules.TakeEvents(), e => e is Core.Systems.Entities.BuildingDestroyed { FocusCamera: false });
    }

    [Fact]
    public void OneTribeMatch_Runs()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, new MatchSetup
        {
            Map = 1,
            Seed = 7,
            Tribes = [new MatchTribe(TribeController.Human(0))],
            EndsWithLastSide = false,
        });
        for (int tick = 0; tick < 3000; tick++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 7));
        }
        Assert.Equal(TribeOutcome.Playing, game.Match.Tribes[0].Outcome);
        Assert.Equal(0, TribeRoles.Computer(game.World));
    }

    [Fact]
    public void FourHumans_GivingEveryOrder_Run()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content,
            MatchWorlds.Setup(1, 31, MatchWorlds.Controllers(4, 0), [0, 1, 2, 3]) with { EndsWithLastSide = false });
        for (int tick = 0; tick < 10000; tick++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 31));
        }
        Assert.Equal(10000, game.Tick);
    }
}
