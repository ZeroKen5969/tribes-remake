using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Systems.Story;
using Tribes.Core.Tests.Systems;
using Tribes.Legacy;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>
/// Free-running comparisons: the original and the new rules start from the same state and each
/// runs its own world updates, frame after frame, with nothing copied between them. State the
/// per-frame comparison copies over (and so cannot see) would make them drift apart.
/// </summary>
[Trait("Speed", "Long")]
public sealed class FreeRunTests
{
    /// <summary>World frames each mission runs from its start.</summary>
    private const int MissionFrames = 6000;

    /// <summary>World frames run from each sampled state.</summary>
    private const int SampleFrames = 1500;

    private readonly ITestOutputHelper output;

    public FreeRunTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void EveryMission_FromItsStart_RunsAlikeOnBothSides()
    {
        var failures = new List<string>();
        int frames = 0;
        for (int mission = 0; mission < LegacySamples.Content.Missions.Count; mission++)
        {
            for (int difficulty = 0; difficulty <= 1; difficulty++)
            {
                GameCanvas menu = ScenarioSamples.Menu.Restore();
                GameSimulation simulation = GameSimulation.StartMission(LegacySamples.Content, mission, difficulty, new MissionOptions
                {
                    RandomState = menu.rnd_a.State,
                    TutorialFlags = menu.b_p,
                    HintsEnabled = menu.i_l == 1,
                    ShowOpening = false,
                });
                GameCanvas game = StartOriginal(menu, mission, difficulty);
                FreeRun run = FreeRun.Compare(game, simulation, MissionFrames, compareEvery: 1);
                frames += run.Frames;
                output.WriteLine($"mission {mission} difficulty {difficulty}: {run}");
                if (run.Difference != null)
                {
                    failures.Add($"mission {mission} difficulty {difficulty}: {run}");
                }
            }
        }
        ScenarioSamples.Menu.Restore();
        output.WriteLine($"{frames} frames run on both sides");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void SampledStates_RunAlikeOnBothSides()
    {
        var failures = new List<string>();
        int frames = 0, starts = 0;
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            GameCanvas game = sample.Restore();
            GameSimulation simulation = GameSimulation.FromWorld(LegacyRules.Import(game, LegacySamples.Content));
            FreeRun run = FreeRun.Compare(game, simulation, SampleFrames, compareEvery: 10);
            frames += run.Frames;
            starts++;
            if (run.Difference != null)
            {
                failures.Add($"{sample}: {run}");
            }
        }
        output.WriteLine($"{starts} starts, {frames} frames run on both sides, {failures.Count} drifted");
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)));
    }

    /// <summary>The original at the menu <paramref name="game"/> with mission <paramref name="mission"/> just started, its world running.</summary>
    private static GameCanvas StartOriginal(GameCanvas game, int mission, int difficulty)
    {
        game.b_b = (sbyte)difficulty;
        game.g_B((sbyte)mission);
        game.b_v = 1;
        return game;
    }
}

/// <summary>One free-running comparison.</summary>
internal sealed class FreeRun
{
    private const int WorldRunning = 1;

    /// <summary>World frames run on both sides.</summary>
    public int Frames { get; private set; }

    /// <summary>The first difference found (frame and fields), or null when the two stayed alike.</summary>
    public string? Difference { get; private set; }

    /// <summary>
    /// Runs <paramref name="frames"/> world updates of <paramref name="game"/> and as many world
    /// frames of <paramref name="simulation"/>, comparing the rule states every
    /// <paramref name="compareEvery"/> frames and at the end. The new side decides on its own whether
    /// its world still runs normally (a scene or a pausing hint stops that, as the original's mode does).
    /// </summary>
    public static FreeRun Compare(GameCanvas game, GameSimulation simulation, int frames, int compareEvery)
    {
        var run = new FreeRun();
        IReadOnlyList<string> diff = Diff(game, simulation);
        if (diff.Count > 0)
        {
            run.Difference = "at the start:\n  " + string.Join("\n  ", diff);
            return run;
        }
        bool running = game.b_v == WorldRunning;
        for (int frame = 1; frame <= frames; frame++)
        {
            game.j_();
            int firstEvent = simulation.Rules.PendingEvents.Count;
            bool? announced = simulation.RunWorldFrame(staged: !running, new TutorialInputs(false));
            running &= !simulation.World.Script.Active && !simulation.Rules.WorldFrame.HintShownSince(firstEvent) && announced is null;
            simulation.Rules.TakeEvents();
            run.Frames = frame;
            if (frame % compareEvery == 0 || frame == frames)
            {
                diff = Diff(game, simulation);
                if (diff.Count > 0)
                {
                    run.Difference = $"frame {frame} (world frame {game.i_c}):\n  " + string.Join("\n  ", diff);
                    return run;
                }
            }
        }
        return run;
    }

    private static IReadOnlyList<string> Diff(GameCanvas game, GameSimulation simulation) =>
        LegacyRules.Capture(game).Differences(WorldRules.Capture(simulation.World), 12);

    /// <inheritdoc />
    public override string ToString() => $"{Frames} frames" + (Difference != null ? ", drifted " + Difference : ", alike");
}
