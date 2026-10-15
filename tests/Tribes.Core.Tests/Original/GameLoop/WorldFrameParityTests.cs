using System.IO;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.Tests.Systems;
using Tribes.Core.World;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>
/// The whole world frame against the original's world update (GC.j_ in rules mode): every update
/// of key-monkey games is run by the new rules on a copy of the state taken at its start.
/// </summary>
[Trait("Speed", "Long")]
public sealed class WorldFrameParityTests
{
    private readonly ITestOutputHelper output;

    public WorldFrameParityTests(ITestOutputHelper output) => this.output = output;

    internal static string Resources => Path.Combine(TestPaths.RepoRoot(), "original", "game", "res");

    [Fact]
    public void EveryFrame_OfMonkeyGame1_MatchesTheOriginal() => EveryFrameMatches(1);

    [Fact]
    public void EveryFrame_OfMonkeyGame4_MatchesTheOriginal() => EveryFrameMatches(4);

    [Fact]
    public void EveryFrame_OfMonkeyGame6_MatchesTheOriginal() => EveryFrameMatches(6);

    [Fact]
    public void EveryFrame_OfEveryMission_MatchesTheOriginal()
    {
        int checkedFrames = 0;
        var failures = new System.Collections.Generic.List<string>();
        for (int mission = 1; mission < LegacySamples.Content.Missions.Count; mission++)
        {
            var game = new MonkeyGame(mission, MissionFrames, Mission: mission, Difficulty: mission % 2, KeepPlaying: true);
            ComparisonRun run = GameComparison.EveryFrame(Resources, LegacySamples.Content, game, Advance);
            output.WriteLine($"{game}: {run}");
            checkedFrames += run.Checked;
            failures.AddRange(run.Failures);
        }
        output.WriteLine($"{checkedFrames} frames checked");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>Display frames each mission is played.</summary>
    private const int MissionFrames = 12000;

    private void EveryFrameMatches(long seed)
    {
        ComparisonRun run = GameComparison.EveryFrame(Resources, LegacySamples.Content, new MonkeyGame(seed, 30000, KeepPlaying: true), Advance);
        output.WriteLine(run.ToString());
        Assert.True(run.Failures.Count == 0, run.ToString());
        Assert.True(run.Checked > 15000, run.ToString());
    }

    /// <summary>The new rules' world frame for an update that started as <paramref name="start"/> says.</summary>
    internal static void Advance(GameWorld world, LegacyFrameStart start) =>
        GameSimulation.FromWorld(world).RunWorldFrame(staged: !start.WorldRunning, new TutorialInputs(start.RosterOpen));
}
