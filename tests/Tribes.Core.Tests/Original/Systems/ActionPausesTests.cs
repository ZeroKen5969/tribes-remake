using Tribes.Core.Systems.Units.Work;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ActionPauses"/> with the original.</summary>
public sealed class ActionPausesTests
{
    private static readonly int[] Actions = { 69, 28, 37, 44, 49, 99, 10 };

    private readonly ITestOutputHelper output;

    public ActionPausesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Pause_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 6))
            {
                foreach (int action in SampleInputs.Pick(Actions, id + sample.Frame, 3))
                {
                    int a = action;
                    run.Check(sample, $"Pause unit {id} action {a}", g => Set(g, ActionField, id, a),
                        g => { BeginTurn(g, id); g.world_a.M_(); },
                        s => s.ActionPauses.Pause(s.World.Unit(id)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }
}
