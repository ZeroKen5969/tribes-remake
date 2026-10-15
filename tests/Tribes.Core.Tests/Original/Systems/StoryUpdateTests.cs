using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// Parity of <see cref="StoryUpdate"/> with the story part of the original's end-of-frame update
/// (the resource nodes, the other part, are emptied on both sides so only the story runs).
/// </summary>
public sealed class StoryUpdateTests
{
    private const int NodeKindBase = 9833, NodeSlots = 50;

    private readonly ITestOutputHelper output;

    public StoryUpdateTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Advance_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 1))
        {
            foreach (int frame in new[] { 4000, 5000, 6000, 8000, 9600, 11000 })
            {
                run.Check(sample, $"Story {frame}", g =>
                {
                    ScenarioSamples.QuietOriginal(g, frame);
                    EmptyNodes(g);
                    g.world_a.i__V();
                }, s =>
                {
                    ScenarioSamples.Quiet(s.World, frame);
                    EmptyNodes(s.World);
                    s.StoryUpdate.Advance(default);
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 150);
    }

    [Fact]
    public void Advance_MatchesTheOriginalAtEveryUnlock()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 1))
        {
            var unlocks = LegacySamples.Content.Rules.Missions.Missions[ScenarioSamples.MissionOf(sample)].TechUnlocks;
            for (int i = 0; i < unlocks.Count; i++)
            {
                int fired = i, frame = unlocks[i].TimeUnits * 100;
                run.Check(sample, $"Unlock {i}", g =>
                {
                    ScenarioSamples.QuietOriginal(g, frame);
                    EmptyNodes(g);
                    g.world_a.b_L = (sbyte)fired;
                    g.world_a.i__V();
                }, s =>
                {
                    ScenarioSamples.Quiet(s.World, frame);
                    EmptyNodes(s.World);
                    s.World.Script.UnlocksFired = fired;
                    s.StoryUpdate.Advance(default);
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 20);
    }

    [Fact]
    public void Advance_UnlocksLevelsWhenQuiet()
    {
        RuleSystems systems = ScenarioSamples.Started(2);
        GameWorld world = systems.World;
        world.Tribes[0][TribeCounter.Chief] = 0;
        world.Frame = 4000;
        world.FramesSinceCombat = 49;
        systems.StoryUpdate.Advance(default);
        Assert.Equal(0, world.Script.UnlocksFired);
        world.FramesSinceCombat = 50;
        world.Script.AwaitingNewChief = true;
        systems.StoryUpdate.Advance(default);
        Assert.Equal(1, world.Script.UnlocksFired);
        Assert.NotNull(systems.CurrentSceneStep);
    }

    private static void EmptyNodes(GameCanvas game)
    {
        for (int i = 0; i < NodeSlots; i++)
        {
            game.bArr_e[NodeKindBase + i] = 0;
        }
    }

    private static void EmptyNodes(GameWorld world)
    {
        foreach (ResourceNode node in world.Map.ResourceNodes)
        {
            node.Kind = 0;
        }
    }
}
