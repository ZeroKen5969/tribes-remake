using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>Teams, players coming and going, giving up, and tribes joining a match.</summary>
public sealed class MatchRulesTests
{
    private readonly ITestOutputHelper output;

    public MatchRulesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void TeamMates_NeverHurtEachOther_WhileEnemiesFight()
    {
        MatchSetup setup = MatchWorlds.Setup(0, 5, MatchWorlds.Controllers(0, 4), teams: [0, 0, 1, 1]);
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, setup);
        int enemyBlows = 0;
        for (int tick = 0; tick < 20000 && !game.IsOver; tick++)
        {
            foreach (BlowLanded blow in game.Step().Events.OfType<BlowLanded>())
            {
                if (blow.StrikerTribe < 0 || blow.Category is not (CellCategories.Unit or CellCategories.Building))
                {
                    continue;
                }
                int victim = blow.Category == CellCategories.Unit
                    ? game.World.TribeOfUnit(blow.Slot) : game.World.TribeOfBuilding(blow.Slot);
                Assert.False(game.World.Relations.AreAllies(blow.StrikerTribe, victim), $"tribe {blow.StrikerTribe} hit ally {victim}");
                enemyBlows++;
            }
        }
        output.WriteLine($"{enemyBlows} blows between enemies by tick {game.Tick}");
        Assert.True(enemyBlows > 0);
        Assert.All(game.Match.Winners, w => Assert.Equal(game.World.Tribes[game.Match.Winners[0]].Team, game.World.Tribes[w].Team));
    }

    [Fact]
    public void AbsentPlayer_GetsNoOrdersAndNoComputer_UntilBack()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 2, MatchWorlds.Controllers(2, 1)));
        game.SetController(1, TribeController.Absent(1));
        (int NextWave, int WaveTarget, int Rebuild) untouched = AiOf(game, 1);
        int unit = MatchWorlds.LiveAdults(game.World, 1).First();
        var move = new MoveUnits(1, UnitSet.Of(unit), game.World.Tribes[1].Camp, 0, false);
        for (int tick = 0; tick < 3000; tick++)
        {
            TickResult result = game.Step(TickInput.WithOrders(move));
            Assert.Equal(OrderOutcome.NotAvailable, result.Outcomes[0]);
        }
        Assert.Equal(untouched, AiOf(game, 1));
        Assert.True(game.World.Tribes[2].Ai.NextWaveFrame > 0, "the computer tribe beside it does play");
        Assert.False(game.IsDefeated(1));
        game.SetController(1, TribeController.Human(1));
        Assert.Equal(OrderOutcome.Applied, game.Step(TickInput.WithOrders(move)).Outcomes[0]);
    }

    [Fact]
    public void Forfeit_IsFinal_PutsTheTribeOut_AndTheMatchGoesOn()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(2, 4, MatchWorlds.Controllers(2, 1)));
        for (int tick = 0; tick < 200; tick++)
        {
            game.Step();
        }
        (int NextWave, int WaveTarget, int Rebuild) untouched = AiOf(game, 0);
        game.SetController(0, TribeController.Forfeited(0));
        Assert.True(game.IsDefeated(0));
        Assert.Equal(TribeOutcome.Forfeited, game.Match.Tribes[0].Outcome);
        Assert.False(game.Match.IsDecided);
        Assert.Throws<InvalidOperationException>(() => game.SetController(0, TribeController.Human(0)));
        int unit = MatchWorlds.LiveAdults(game.World, 0).First();
        TickResult result = game.Step(TickInput.WithOrders(new MoveUnits(0, UnitSet.Of(unit), new GridPoint(5, 5), 0, false)));
        Assert.Equal(OrderOutcome.NotAvailable, result.Outcomes[0]);
        Assert.Equal(untouched, AiOf(game, 0));
        Assert.Contains(result.Events, e => e is TribeOut);
        game.SetController(1, TribeController.Forfeited(1));
        Assert.True(game.Match.IsDecided);
        Assert.Equal(new[] { 2 }, game.Match.Winners);
        while (!game.IsOver)
        {
            game.Step();
        }
        Assert.Equal(TribeOutcome.Won, game.Match.Tribes[2].Outcome);
    }

    [Fact]
    public void Controllers_StayOnTheirSide()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(3, 1, MatchWorlds.Controllers(1, 1)));
        Assert.Throws<InvalidOperationException>(() => game.SetController(0, TribeController.Computer()));
        Assert.Throws<InvalidOperationException>(() => game.SetController(1, TribeController.Human(3)));
        Assert.Throws<ArgumentOutOfRangeException>(() => game.SetController(2, TribeController.Human(3)));
        game.SetController(1, TribeController.Computer(0));
        Assert.Equal(0, TribeRoles.Difficulty(game.World, 1));
        Assert.Throws<ArgumentException>(() => GameSimulation.StartMatch(MatchWorlds.Content, new MatchSetup()));
        Assert.Throws<ArgumentException>(() => GameSimulation.StartMatch(MatchWorlds.Content,
            MatchWorlds.Setup(99, 1, MatchWorlds.Controllers(1, 1))));
    }

    [Fact]
    public void AddTribe_MidGame_GetsTheNextSlotsAndACampFarAway()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 8, MatchWorlds.Controllers(1, 2)) with { Settings = new WorldSettings { OpeningGraceFrames = 2500 } });
        for (int tick = 0; tick < 1500; tick++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 8));
        }
        int units = game.World.UnitSlots, buildings = game.World.BuildingSlots;
        int[] before = Enumerable.Range(1, units).Select(id => game.World.Unit(id).HitPoints).ToArray();
        int human = game.AddTribe(TribeController.Human(1), team: 7);
        int computer = game.AddTribe(TribeController.Computer(1));
        Assert.Equal((3, 4), (human, computer));
        GameWorld world = game.World;
        int[] added = [human, computer];
        Assert.All(added, t => Assert.All(world.UnitsOf(t), unit => Assert.True(unit.Id > units)));
        Assert.All(added, t => Assert.All(world.BuildingsOf(t), building => Assert.True(building.Id > buildings)));
        Assert.Equal(units + world.UnitsOf(human).Count + world.UnitsOf(computer).Count, world.UnitSlots);
        Assert.Equal(buildings + world.BuildingsOf(human).Count + world.BuildingsOf(computer).Count, world.BuildingSlots);
        Assert.Equal(before, Enumerable.Range(1, units).Select(id => game.World.Unit(id).HitPoints).ToArray());
        Assert.Equal(7, game.World.Tribes[human].Team);
        Assert.True(MatchWorlds.LiveAdults(game.World, human).Count() > 5);
        Assert.True(game.World.Tribes[human][TribeCounter.Chief] > 0);
        foreach (TribeState other in game.World.Tribes.Take(3))
        {
            GridPoint camp = game.World.Tribes[human].Camp;
            Assert.True(Math.Abs(camp.X - other.Camp.X) + Math.Abs(camp.Y - other.Camp.Y) > 10);
        }
        for (int tick = 0; tick < 4000; tick++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 9));
        }
        GameSimulation loaded = GameSimulation.Load(MatchWorlds.Content, game.Save());
        Assert.Equal(game.StateHash(), loaded.StateHash());
        Assert.Equal(5, loaded.World.Tribes.Count);
        Assert.True(game.World.Tribes[computer][TribeCounter.Buildings] > 2, "the new computer tribe builds");
    }

    [Fact]
    public void AddTribe_NeedsAMatch_AndRoom()
    {
        GameSimulation campaign = GameSimulation.StartMission(MatchWorlds.Content, 7, 0);
        Assert.Throws<InvalidOperationException>(() => campaign.AddTribe(TribeController.Computer()));
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(4, 1, MatchWorlds.Controllers(0, 2)));
        int added = 0;
        while (game.AddTribe(TribeController.Computer()) >= 0)
        {
            added++;
            Assert.True(added < 200, "camps never stop fitting");
        }
        output.WriteLine($"{added} more camps fitted on map 4");
        Assert.True(added > 0);
    }

    /// <summary>What the computer would change in a tribe's AI state if it played the tribe.</summary>
    private static (int NextWave, int WaveTarget, int Rebuild) AiOf(GameSimulation game, int tribe)
    {
        AiState ai = game.World.Tribes[tribe].Ai;
        return (ai.NextWaveFrame, ai.WaveTarget, ai.RebuildSlot);
    }
}
