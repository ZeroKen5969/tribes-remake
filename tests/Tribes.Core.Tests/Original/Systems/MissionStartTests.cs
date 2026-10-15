using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="MissionStart"/> and <see cref="MissionStarter"/> with the original's mission start, plus edge cases.</summary>
public sealed class MissionStartTests
{
    private const int Missions = 12, FirstCustom = 7, OpeningMode = 28, ChapterMode = 18;

    private readonly ITestOutputHelper output;

    public MissionStartTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Begin_MatchesTheOriginalStartOfEveryMission()
    {
        var failures = new List<string>();
        int compared = 0;
        LegacySample menu = ScenarioSamples.Menu;
        for (int mission = 0; mission < Missions; mission++)
        {
            for (int difficulty = 0; difficulty <= 1; difficulty++)
            {
                foreach (int tutorial in mission < 2 ? new[] { 0, 1 } : new[] { 0 })
                {
                    IReadOnlyList<string> diff = CompareBegin(menu, mission, difficulty, tutorial);
                    compared++;
                    if (diff.Count > 0)
                    {
                        failures.Add($"mission {mission} difficulty {difficulty} tutorial {tutorial}:\n  " + string.Join("\n  ", diff));
                    }
                }
            }
        }
        output.WriteLine($"{compared} mission starts compared");
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(4)));
    }

    [Fact]
    public void Start_MatchesTheOriginalOnPlayedWorlds()
    {
        var run = new ParityRun();
        int i = 0;
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 6))
        {
            int mission = (i++ * 5) % Missions;
            run.Check(sample, $"Start {mission}", g => g.g_B((sbyte)mission), s => s.MissionStarter.Start(mission));
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 15);
    }

    [Fact]
    public void Begin_PlaysTheOpeningOnTheChapterScreen()
    {
        RuleSystems systems = MissionStart.Begin(LegacySamples.Content, 2, 0);
        SceneStep? step = systems.CurrentSceneStep;
        Assert.NotNull(step);
        Assert.Equal(LegacySamples.Content.Rules.Missions.Missions[2].OpeningDialogue, step!.Dialogue);
        Assert.True(step.ChapterScreen);
        Assert.False(step.Staged);
        Assert.Equal(SceneFrame.Hold, systems.AdvanceSceneStep());
        Assert.Contains(systems.TakeEvents(), e => e is SceneStepShown);
    }

    [Fact]
    public void Begin_WithoutOpening_StartsInPlay()
    {
        RuleSystems systems = MissionStart.Begin(LegacySamples.Content, 4, 1, new MissionOptions { ShowOpening = false, Seed = 7 });
        GameWorld world = systems.World;
        Assert.Null(systems.CurrentSceneStep);
        Assert.Equal(4, world.Mission);
        Assert.Equal(1, world.Difficulty);
        Assert.Equal(0, world.Frame);
        Assert.Equal(MissionStarter.FirstChief, world.Tribes[0][TribeCounter.Chief]);
        Assert.True(world.Tribes[1][TribeCounter.Population] > 0);
        Assert.Equal(-1, world.Ai.WaveTarget);
    }

    [Fact]
    public void Begin_CustomMissionsHaveNoOpening()
    {
        RuleSystems systems = MissionStart.Begin(LegacySamples.Content, FirstCustom + 1, 0);
        Assert.Null(systems.CurrentSceneStep);
        Assert.Equal(MissionStarter.FirstChief, systems.World.Tribes[0][TribeCounter.Chief]);
    }

    [Fact]
    public void Begin_RejectsUnknownMissions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MissionStart.Begin(LegacySamples.Content, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MissionStart.Begin(LegacySamples.Content, Missions + 3, 0));
        Assert.Throws<ArgumentNullException>(() => MissionStart.Begin(null!, 0, 0));
    }

    [Fact]
    public void KeepsStock_OnlyForTheSecondParts()
    {
        Assert.True(MissionSetup.KeepsStock(1));
        Assert.True(MissionSetup.KeepsStock(6));
        Assert.False(MissionSetup.KeepsStock(0));
        Assert.False(MissionSetup.KeepsStock(5));
    }

    private static IReadOnlyList<string> CompareBegin(LegacySample menu, int mission, int difficulty, int tutorial)
    {
        int? opening = LegacySamples.Content.Rules.Missions.Missions[mission].OpeningDialogue;
        bool show = opening.HasValue && mission < FirstCustom;
        return ScenarioSamples.CompareNewWorld(menu,
            g =>
            {
                g.b_b = (sbyte)difficulty;
                g.b_p = (sbyte)tutorial;
                g.g_B((sbyte)mission);
                if (show)
                {
                    g.b_Q = OpeningMode;
                    g.b_I_m(opening!.Value);
                    g.b_Q = ChapterMode;
                }
            },
            g => MissionStart.Begin(LegacySamples.Content, mission, difficulty, new MissionOptions
            {
                RandomState = g.rnd_a.State,
                TutorialFlags = tutorial,
                HintsEnabled = g.i_l == 1,
            }));
    }
}
