using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.Tests.Commands;
using Tribes.Core.Tests.Systems;
using Tribes.Core.World;
using Tribes.Legacy;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>
/// The game loop free-running beside the original: <see cref="GameSimulation"/> gets, pass by
/// pass, only the orders the original's input handlers carried out (through the players' orders)
/// and whether the host held the game, and must stay identical to the original game, including
/// when it runs a world frame, plays a scene step or holds.
/// </summary>
[Trait("Speed", "Long")]
public sealed class ReplayTests
{
    private readonly ITestOutputHelper output;

    public ReplayTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void MonkeyGame1_RunsAlikePassByPass() => RunsAlike(new MonkeyGame(1, 30000, KeepPlaying: true));

    [Fact]
    public void MonkeyGame4_RunsAlikePassByPass() => RunsAlike(new MonkeyGame(4, 30000, KeepPlaying: true));

    [Fact]
    public void MonkeyGame6_RunsAlikePassByPass() => RunsAlike(new MonkeyGame(6, 30000, KeepPlaying: true));

    [Fact]
    public void EveryMission_RunsAlikePassByPass()
    {
        var failures = new List<string>();
        int passes = 0;
        for (int mission = 1; mission < LegacySamples.Content.Missions.Count; mission++)
        {
            ReplayRun run = Replay(new MonkeyGame(mission, 12000, Mission: mission, Difficulty: mission % 2, KeepPlaying: true));
            passes += run.Passes;
            if (run.Differed > 0)
            {
                failures.Add(run.ToString());
            }
        }
        output.WriteLine($"{passes} passes followed");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void OrdersOfLongMonkeyGames_DoWhatTheOriginalHandlersDo()
    {
        var failures = new List<string>();
        foreach (long seed in new[] { 1L, 4L, 6L })
        {
            InputRun run = InputComparison.Run(WorldFrameParityTests.Resources, LegacySamples.Content,
                new MonkeyGame(seed, 30000, KeepPlaying: true),
                (game, report, world) => OriginalOrders.Rewrite(LegacySamples.Content, report, world));
            output.WriteLine($"seed {seed}: {run}");
            if (run.FailureCount > 0 || run.UnexplainedCount > 0)
            {
                failures.Add($"seed {seed}: {run}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private void RunsAlike(MonkeyGame game)
    {
        ReplayRun run = Replay(game);
        Assert.True(run.Differed == 0, run.ToString());
        Assert.True(run.WorldFrames > 15000, run.ToString());
    }

    private ReplayRun Replay(MonkeyGame game)
    {
        ReplayRun run = ReplayComparison.Run(WorldFrameParityTests.Resources, LegacySamples.Content, game,
            new SimulationReplay(LegacySamples.Content));
        output.WriteLine($"{game}: {run}");
        return run;
    }

    /// <summary>A game simulation following the original's passes.</summary>
    private sealed class SimulationReplay : IReplayTarget
    {
        private readonly GameContent content;
        private GameSimulation? simulation;

        public SimulationReplay(GameContent content) => this.content = content;

        public GameWorld World => simulation!.World;

        public void Reset(GameWorld world)
        {
            // The scene's step timer and screen are not part of the compared rule state; take them over too.
            GameCanvas g = GameCanvas.current;
            world.Script.StepFrame = g.z_j ? ScenePlayer.ContinuesByItself : g.i_aR;
            world.Script.ChapterScreen = g.z_s && g.b_Q == ChapterScreen;
            simulation = GameSimulation.FromWorld(world);
        }

        public bool Step(IReadOnlyList<int[]> reports, ReplayPass pass)
        {
            PlayerOrder[] orders = reports.Select(r => OriginalOrders.From(content, r)).OfType<GameOrder>()
                .Select(PlayerOrder.From).ToArray();
            TickResult result = simulation!.Step(new TickInput { Orders = orders, Paused = pass.Paused, RosterOpen = pass.RosterOpen });
            return result.WorldAdvanced;
        }

        private const int ChapterScreen = 18;
    }
}
