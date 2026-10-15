using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="MissionOutcome"/> with the original's victory and defeat rules, plus the countdown.</summary>
public sealed class MissionOutcomeTests
{
    private const int Buildings = 14, Population = 11, FirstBuildingHp = 8506;

    private readonly ITestOutputHelper output;

    public MissionOutcomeTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Check_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            foreach ((int enemyBuildings, int enemyPop, int playerPop, bool firstLost, int countdown) in new[]
            {
                (5, 3, 4, false, 0), (0, 0, 4, false, 0), (3, 0, 4, false, 0), (3, 0, 4, false, 20), (5, 3, 0, false, 0),
                (5, 3, 4, true, 0), (0, 0, 0, true, 7),
            })
            {
                run.Check(sample, $"Outcome {enemyBuildings}:{enemyPop}:{playerPop}:{firstLost}:{countdown}", g =>
                {
                    (g.sArr2_a[1][Buildings], g.sArr2_a[1][Population], g.sArr2_a[0][Population]) =
                        ((short)enemyBuildings, (short)enemyPop, (short)playerPop);
                    g.b_u = (sbyte)countdown;
                    if (firstLost)
                    {
                        g.bArr_e[FirstBuildingHp] = 0;
                    }
                    g.world_a.ak_();
                }, s =>
                {
                    GameWorld w = s.World;
                    (w.Tribes[1][TribeCounter.Buildings], w.Tribes[1][TribeCounter.Population], w.Tribes[0][TribeCounter.Population]) =
                        (enemyBuildings, enemyPop, playerPop);
                    w.EndCountdown = countdown;
                    if (firstLost)
                    {
                        w.Building(1).HitPoints = 0;
                    }
                    s.MissionOutcome.Check();
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 100);
    }

    [Fact]
    public void AdvanceCountdown_RunsOnNormalFramesAndAnnouncesTheResult()
    {
        RuleSystems systems = ScenarioSamples.Started(2);
        GameWorld world = systems.World;
        Assert.Null(systems.MissionOutcome.AdvanceCountdown(true));
        Assert.Equal(0, world.EndCountdown);
        world.EndCountdown = MissionOutcome.VictoryStart;
        Assert.Null(systems.MissionOutcome.AdvanceCountdown(false));
        Assert.Equal(MissionOutcome.VictoryStart, world.EndCountdown);
        bool? result = null;
        while (result is null)
        {
            result = systems.MissionOutcome.AdvanceCountdown(true);
        }
        Assert.True(result);
        Assert.Equal(MissionOutcome.VictoryShown, world.EndCountdown);
        Assert.Contains(systems.TakeEvents(), e => e is MissionEnded { Victory: true });
        world.EndCountdown = MissionOutcome.DefeatShown - 1;
        Assert.False(systems.MissionOutcome.AdvanceCountdown(true));
    }
}
