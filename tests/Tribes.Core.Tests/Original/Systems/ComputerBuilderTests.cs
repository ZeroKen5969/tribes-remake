using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ComputerBuilder"/> with the original's building plan, plus edge cases.</summary>
public sealed class ComputerBuilderTests
{
    private const int HitPoints = 8505, BuildingSlots = 60;

    private readonly ITestOutputHelper output;

    public ComputerBuilderTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void CheckBuildPlan_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in AiSamples.States(everyNth: 1))
        {
            int interval = AiSamples.Ai(sample).BuildPlanCheckInterval * 10;
            foreach (int frame in new[] { 32 * 40, interval * 7, (interval * 3) + 1 })
            {
                foreach ((bool damaged, bool emptied) in new[] { (false, false), (true, false), (false, true) })
                {
                    var plan = AiSamples.Ai(sample).BuildPlan;
                    run.Check(sample, $"Plan {frame}:{damaged}:{emptied}", g =>
                    {
                        g.i_c = frame;
                        if (damaged)
                        {
                            Damage(g);
                        }
                        if (emptied && plan.Count > 0)
                        {
                            (g.bArr2_c[plan[0].Cell.Y][plan[0].Cell.X], g.world_a.b_P) = (0, -1);
                        }
                        g.world_a.i_u = AiSamples.Computer;
                        g.world_a.ar_();
                    }, s =>
                    {
                        s.World.Frame = frame;
                        if (damaged)
                        {
                            Damage(s);
                        }
                        if (emptied && plan.Count > 0)
                        {
                            s.World.Map.SetCell(plan[0].Cell, Tribes.Core.Maps.MapCell.Empty);
                            s.World.Ai.RebuildSlot = -1;
                        }
                        s.ComputerBuilder.CheckBuildPlan(AiSamples.Computer);
                    });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void FirstEntryNumber_CountsTheEarlierMissionsPlans()
    {
        RuleSystems systems = ScenarioSamples.Started(4);
        var missions = LegacySamples.Content.Rules.Missions.Missions;
        Assert.Equal(missions.Take(4).Sum(m => m.Ai.BuildPlan.Count), systems.ComputerBuilder.FirstEntryNumber());
    }

    /// <summary>Every computer building drops to 1 hit point (so repairs are due).</summary>
    private static void Damage(Tribes.Legacy.GameCanvas game)
    {
        for (int id = 31; id <= BuildingSlots; id++)
        {
            if (game.bArr_e[HitPoints + id] > 0)
            {
                game.bArr_e[HitPoints + id] = 1;
            }
        }
    }

    private static void Damage(RuleSystems systems)
    {
        for (int id = 31; id <= BuildingSlots; id++)
        {
            if (systems.World.Building(id).HitPoints > 0)
            {
                systems.World.Building(id).HitPoints = 1;
            }
        }
    }
}
