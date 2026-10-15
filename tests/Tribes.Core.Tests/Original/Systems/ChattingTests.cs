using System.Linq;
using Tribes.Core.Systems.Units.Animation;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Chatting"/> with the original.</summary>
public sealed class ChattingTests
{
    private const int FacingField = 6;

    private readonly ITestOutputHelper output;

    public ChattingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void StartChatAndAnswer_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 8).Where(id => UnitSampling.OnItsCell(game, id)))
            {
                run.Check(sample, $"StartChat unit {id}",
                    g => UnitSampling.Current(g, id).g_(),
                    s => s.Chatting.StartChat(s.World.Unit(id)));
                foreach (int facing in SampleInputs.Pick(Enumerable.Range(0, 8).ToArray(), id, 3))
                {
                    run.Check(sample, $"Answer unit {id} facing {facing}",
                        g => { UnitSampling.Set(g, FacingField, id, facing); UnitSampling.Current(g, id).z_(); },
                        s => { s.World.Unit(id).Facing = facing; s.Chatting.Answer(s.World.Unit(id)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 900);
    }
}
