using System;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Story;
using Tribes.Core.Tests.Commands;
using Tribes.Core.Tests.Systems;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>What each kind of tick does: play, story scenes, held ticks, the mission's end, orders and their outcomes.</summary>
public sealed class TickModeTests
{
    private const int Player = 0;

    [Fact]
    public void PlayTick_CarriesOutTheOrdersThenRunsOneWorldFrame()
    {
        GameSimulation game = Started(2);
        int frame = game.World.Frame;
        TickResult result = game.Step(TickInput.WithOrders(new SetHints(Player, false)));
        Assert.Equal(0, result.Tick);
        Assert.Equal(1, game.Tick);
        Assert.Equal(TickKind.Play, result.Kind);
        Assert.True(result.WorldAdvanced);
        Assert.Equal(new[] { OrderOutcome.Applied }, result.Outcomes);
        Assert.False(game.World.Hints.Enabled);
        Assert.Equal(frame + 1, game.World.Frame);
        Assert.Null(result.Victory);
        Assert.Equal(TickKind.Play, game.NextKind);
    }

    [Fact]
    public void PausedTick_OnlyCarriesOutTheOrders()
    {
        GameSimulation game = Started(3);
        game.Step();
        ulong before = game.StateHash();
        TickResult held = game.Step(new TickInput { Paused = true });
        Assert.Equal(TickKind.Paused, held.Kind);
        Assert.False(held.WorldAdvanced);
        Assert.Equal(before, game.StateHash());
        TickResult ordered = game.Step(new TickInput { Paused = true, Orders = new[] { PlayerOrder.From(new SetHints(Player, false)) } });
        Assert.Equal(new[] { OrderOutcome.Applied }, ordered.Outcomes);
        Assert.NotEqual(before, game.StateHash());
        Assert.Equal(3, game.Tick);
    }

    [Fact]
    public void Payloads_AreDecodedForTheSendingTribe()
    {
        GameSimulation game = Started(2);
        byte[] payload = GameOrderCodec.Encode(new SetHints(Player, false));
        TickResult result = game.Step(new TickInput
        {
            Orders = new[] { PlayerOrder.FromPayload(Player, payload), PlayerOrder.FromPayload(Player, new byte[] { 1, 250 }) },
        });
        Assert.Equal(new[] { OrderOutcome.Applied, OrderOutcome.Malformed }, result.Outcomes);
        Assert.False(game.World.Hints.Enabled);
        Assert.Equal(Player, PlayerOrder.FromPayload(Player, payload).Tribe);
        Assert.Null(PlayerOrder.FromPayload(Player, payload).Order);
        Assert.Throws<ArgumentNullException>(() => PlayerOrder.From(null!));
        Assert.Throws<ArgumentNullException>(() => game.Step(null!));
        long tick = game.Tick;
        Assert.Throws<ArgumentNullException>(() => game.Step(new TickInput { Orders = new PlayerOrder[] { null! } }));
        Assert.Equal(tick, game.Tick);
        Assert.Throws<ArgumentNullException>(() => TickInput.WithOrders(null!));
    }

    [Fact]
    public void OpeningScene_IsPlayedByTicks_TapsAndStagedFrames()
    {
        GameSimulation game = GameSimulation.StartMission(LegacySamples.Content, 0, 0);
        Assert.Equal(TickKind.Scene, game.NextKind);
        Assert.NotNull(game.CurrentScene);
        TickResult first = game.Step();
        Assert.Contains(first.Events, e => e is SceneStepShown);
        int sceneTicks = 0, taps = 0;
        while (game.World.Script.Active && sceneTicks < 5000)
        {
            bool tap = game.CanContinueScene;
            TickResult result = game.Step(tap ? TickInput.WithOrders(new AdvanceScene(Player)) : TickInput.Idle);
            Assert.Equal(TickKind.Scene, result.Kind);
            taps += tap ? 1 : 0;
            sceneTicks++;
        }
        Assert.False(game.World.Script.Active);
        Assert.True(taps > 0);
        Assert.Equal(TickKind.Play, game.NextKind);
        Assert.Equal(new[] { OrderOutcome.NotAvailable }, game.Step(TickInput.WithOrders(new AdvanceScene(Player))).Outcomes);
    }

    [Fact]
    public void StagedSteps_RunWorldFramesAndGoOnByThemselves()
    {
        GameSimulation game = Started(2);
        int silent = SilentStagedDialogue();
        game.Rules.Scenes.Start(silent, chapterScreen: false);
        SceneStep shown = game.CurrentScene!;
        Assert.True(shown.Staged && shown.WorldFrames > 0 && shown.Speaker == 0, $"dialogue {silent}: {shown}");
        int frame = game.World.Frame;
        int advanced = 0;
        while (game.World.Script.StepFrame != ScenePlayer.ContinuesByItself)
        {
            TickResult result = game.Step();
            Assert.Equal(TickKind.Scene, result.Kind);
            Assert.False(game.CanContinueScene && game.World.Script.StepFrame > 0);
            advanced += result.WorldAdvanced ? 1 : 0;
        }
        Assert.Equal(shown.WorldFrames, advanced);
        Assert.Equal(frame + advanced, game.World.Frame);
        TickResult next = game.Step();
        Assert.Contains(next.Events, e => e is SceneStepShown or SceneEnded);
    }

    [Fact]
    public void HeldScene_DoesNotGoOnByItself()
    {
        GameSimulation game = Started(2);
        game.Rules.Scenes.Start(SilentStagedDialogue(), chapterScreen: false);
        int step = game.World.Script.Step;
        for (int i = 0; i < 400; i++)
        {
            Assert.Equal(TickKind.Paused, game.Step(new TickInput { Paused = true }).Kind);
        }
        Assert.Equal(step, game.World.Script.Step);
    }

    [Fact]
    public void TheEnd_IsAnnouncedOnceWithTheScore_ThenNothingChanges()
    {
        GameSimulation game = Started(2);
        game.World.EndCountdown = MissionOutcome.DefeatShown - 1;
        game.World.Tribes[Player][TribeCounter.Born] = 20;
        int score = game.World.Tribes[Player][TribeCounter.Score];
        TickResult end = game.Step();
        Assert.False(end.Victory);
        Assert.Contains(end.Events, e => e is MissionEnded { Victory: false });
        Assert.True(game.IsOver);
        Assert.False(game.Victory);
        Assert.Equal(TickKind.Over, game.NextKind);
        TribeState player = game.World.Tribes[Player];
        int expected = score;
        foreach (var weight in LegacySamples.Content.Rules.Missions.ScoreWeights)
        {
            expected += player[weight.Counter] * weight.WeightTenths / 10;
        }
        Assert.Equal(expected, player[TribeCounter.Score]);
        Assert.True(player[TribeCounter.Score] >= score + (20 * 15 / 10));
        ulong hash = game.StateHash();
        TickResult after = game.Step(TickInput.WithOrders(new SetHints(Player, false)));
        Assert.Equal(TickKind.Over, after.Kind);
        Assert.False(after.WorldAdvanced);
        Assert.Equal(new[] { OrderOutcome.NotAvailable }, after.Outcomes);
        Assert.Equal(hash, game.StateHash());
    }

    [Fact]
    public void Victory_IsAnnouncedAtItsCountdownsEnd()
    {
        GameSimulation game = Started(3);
        game.World.EndCountdown = MissionOutcome.VictoryShown - 1;
        Assert.Null(game.Victory);
        Assert.True(game.Step().Victory);
        Assert.True(game.Victory);
    }

    [Fact]
    public void HintShownByAnOrder_HoldsTheWorldForTheRestOfTheTick()
    {
        foreach (bool hintsPause in new[] { true, false })
        {
            var w = new OrderWorld();
            w.World.Hints.Enabled = true;
            w.World.FramesSinceCombat = 50;
            Unit unit = w.Unit(3, 5, 5);
            unit.Happiness = 9;
            w.Building(BuildingKindNumbers.Store, 20, 20);
            GameSimulation game = GameSimulation.FromWorld(w.World, 0, new SimulationOptions { HintsPauseGame = hintsPause });
            TickResult result = game.Step(TickInput.WithOrders(new MoveUnits(Player, UnitSet.Of(3), new GridPoint(30, 20), 0, false)));
            Assert.Contains(result.Events, e => e is HintEvent);
            Assert.Equal(!hintsPause, result.WorldAdvanced);
        }
    }

    [Fact]
    public void SaveLoad_KeepsTickAndOptions()
    {
        GameSimulation game = GameSimulation.StartMission(LegacySamples.Content, 2, 1, new MissionOptions { ShowOpening = false },
            new SimulationOptions { HintsPauseGame = false });
        for (int i = 0; i < 25; i++)
        {
            game.Step();
        }
        GameSimulation loaded = GameSimulation.Load(LegacySamples.Content, game.Save());
        Assert.Equal(25, loaded.Tick);
        Assert.False(loaded.Options.HintsPauseGame);
        Assert.True(GameSimulation.Load(LegacySamples.Content, game.Save(), new SimulationOptions { HintsPauseGame = true }).Options.HintsPauseGame);
        Assert.Equal(game.StateHash(), loaded.StateHash());
        Assert.Throws<ArgumentNullException>(() => GameSimulation.Load(null!, new byte[1]));
        Assert.Throws<ArgumentNullException>(() => GameSimulation.Load(LegacySamples.Content, (byte[])null!));
        Assert.Throws<ArgumentNullException>(() => game.Save(null!));
    }

    /// <summary>A dialogue whose first step is staged, runs frames and has nobody speaking.</summary>
    private static int SilentStagedDialogue()
    {
        StoryTablesView story = new(LegacySamples.Content);
        return story.FirstSilentStagedDialogue();
    }

    private static GameSimulation Started(int mission) =>
        GameSimulation.StartMission(LegacySamples.Content, mission, 0, new MissionOptions { ShowOpening = false });

    /// <summary>Looks up dialogues by how their first step is played.</summary>
    private sealed class StoryTablesView
    {
        private readonly GameContent content;

        public StoryTablesView(GameContent content) => this.content = content;

        public int FirstSilentStagedDialogue()
        {
            var story = content.Rules.Story;
            return Enumerable.Range(0, story.Dialogues.Count).First(d =>
            {
                var first = story.Pages[story.Dialogues[d].FirstPage];
                var scene = story.Scenes[first.Scene];
                return first.Speaker == 0 && first.Scene > 0 && scene.Mode >= 2 && scene.DurationFrames > 0 && d != 4;
            });
        }
    }
}
