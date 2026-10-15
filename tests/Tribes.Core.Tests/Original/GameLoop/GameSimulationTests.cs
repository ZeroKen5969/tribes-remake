using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Tests.Systems;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>The game loop's public API: ticks, frame modes, orders and events.</summary>
public sealed class GameSimulationTests
{
    private readonly ITestOutputHelper output;

    public GameSimulationTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void EveryMission_PlaysThroughItsOpeningAndRuns()
    {
        for (int mission = 0; mission < LegacySamples.Content.Missions.Count; mission++)
        {
            GameSimulation game = GameSimulation.StartMission(LegacySamples.Content, mission, 0);
            int frames = 0, scenes = 0;
            for (int i = 0; i < 2000 && !game.IsOver; i++)
            {
                TickInput input = game.CanContinueScene ? TickInput.WithOrders(new AdvanceScene(0)) : TickInput.Idle;
                TickResult result = game.Step(input);
                frames += result.WorldAdvanced ? 1 : 0;
                scenes += result.Kind == TickKind.Scene ? 1 : 0;
            }
            output.WriteLine($"mission {mission}: frame {game.World.Frame}, {frames} world frames, {scenes} scene ticks, tick {game.Tick}");
            Assert.True(frames > 0);
        }
    }

    [Fact]
    public void TitleScreenFigures_NeverReachAGame()
    {
        // The title screen, drawn while no mission is in progress, poses units 1..3 as its figures
        // (rule state of the original). Starting a mission clears them: the started game is the
        // one the new rules start from nothing.
        Legacy.GameCanvas menu = ScenarioSamples.Menu.Restore();
        Assert.Equal(-1, menu.i_c);
        Assert.Equal(new[] { 39, 42, 42 }, Enumerable.Range(1, 3).Select(id => (int)menu.bArr_e[Parity.EntityFields.UnitBase + (29 * Parity.EntityFields.UnitStride) + id]));
        long random = menu.rnd_a.State;
        IReadOnlyList<string> diff = ScenarioSamples.CompareNewWorld(ScenarioSamples.Menu, g => g.g_B(2),
            g => GameSimulation.StartMission(LegacySamples.Content, 2, 0,
                new MissionOptions { RandomState = random, TutorialFlags = g.b_p, HintsEnabled = g.i_l == 1, ShowOpening = false }).Rules);
        Assert.True(diff.Count == 0, string.Join("\n", diff));
    }
}
