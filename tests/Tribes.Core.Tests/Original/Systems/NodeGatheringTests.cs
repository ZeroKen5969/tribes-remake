using System;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="NodeGathering"/> with the original.</summary>
public sealed class NodeGatheringTests
{
    private static readonly int[] Actions = { 10, 12, 13, 15, 16, 11, 14, 69, 60 };

    private readonly ITestOutputHelper output;

    public NodeGatheringTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void GoOnAndGoToNearbyNode_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 5))
            {
                foreach (int action in SampleInputs.Pick(Actions, id + sample.Frame, 3))
                {
                    int a = action, carried = (id + action) % 2 * 2;
                    Action<GameCanvas> prepare = g => { Set(g, ActionField, id, a); Set(g, CarriedAmount, id, carried); };
                    run.Check(sample, $"GoOn unit {id} action {a}", prepare,
                        g => { BeginTurn(g, id); g.world_a.an_(); },
                        s => s.NodeGathering.GoOn(TargetCycleTests.Turn(s, id)));
                    bool fromHere = (id + a) % 3 == 0;
                    run.Check(sample, $"GoToNearbyNode unit {id} action {a}", prepare,
                        g => { BeginTurn(g, id); return g.world_a.b_Z_Z(fromHere); },
                        s => s.NodeGathering.GoToNearbyNode(s.World.Unit(id), fromHere));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }
}
