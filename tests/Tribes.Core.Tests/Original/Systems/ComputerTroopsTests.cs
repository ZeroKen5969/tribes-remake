using Tribes.Core.Systems.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ComputerTroops"/> with the original's handling of the computer's units.</summary>
public sealed class ComputerTroopsTests
{
    private const int StatusBase = 7979, ArmyBit = 0x40, HiddenBit = 0x02;

    private readonly ITestOutputHelper output;

    public ComputerTroopsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Update_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in AiSamples.States(everyNth: 1))
        {
            foreach (int frame in AiSamples.Frames(sample))
            {
                foreach (bool army in new[] { false, true })
                {
                    run.Check(sample, $"Troops {frame}:{army}", g =>
                    {
                        g.i_c = frame;
                        if (army)
                        {
                            MakeArmy(g);
                        }
                        AiSamples.Registers(g, sample);
                        g.world_a.as_();
                    }, s =>
                    {
                        s.World.Frame = frame;
                        if (army)
                        {
                            MakeArmy(s.World);
                        }
                        s.ComputerTroops.Update(AiSamples.Computer, AiSamples.Ai(sample).Posts.Count,
                            AiSamples.RampUp(sample));
                    });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    /// <summary>Every second computer unit is a hidden army unit.</summary>
    private static void MakeArmy(Tribes.Legacy.GameCanvas game)
    {
        for (int id = 51; id <= 100; id += 2)
        {
            game.bArr_e[StatusBase + id] = (sbyte)(game.bArr_e[StatusBase + id] | ArmyBit | HiddenBit);
        }
    }

    private static void MakeArmy(World.GameWorld world)
    {
        for (int id = 51; id <= 100; id += 2)
        {
            world.Unit(id).Status |= World.UnitStatus.Mustered | World.UnitStatus.Hidden;
        }
    }
}
