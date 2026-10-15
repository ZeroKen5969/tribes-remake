using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ComputerOrders"/> with the original's armoury, trap and spell orders.</summary>
public sealed class ComputerOrdersTests
{
    private const int TrapAreaTable = 266, SpellAreaTable = 271, DestinationX = 3131, DestinationY = 3232, Pending = 9;

    private readonly ITestOutputHelper output;

    public ComputerOrdersTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void PickCell_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in AiSamples.States(everyNth: 1))
        {
            MissionAi ai = AiSamples.Ai(sample);
            foreach ((int table, var areas) in new[] { (TrapAreaTable, ai.TrapAreas), (SpellAreaTable, ai.SpellTargetAreas) })
            {
                if (areas.Count == 0)
                {
                    continue;
                }
                run.Check(sample, $"PickCell {table}", g =>
                {
                    g.world_a.n_I(table);
                    return new GridPoint(g.bArr_e[DestinationX], g.bArr_e[DestinationY]);
                }, s => s.ComputerOrders.PickCell(areas));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 40);
    }

    [Fact]
    public void Orders_MatchTheOriginalWhenArmouriesAreIdle()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in AiSamples.States(everyNth: 1))
        {
            MissionAi ai = AiSamples.Ai(sample);
            int armoury = (ai.ArmouryOrderInterval * 50 * 40) + 50;
            run.Check(sample, "Orders", g =>
            {
                ClearQueues(g);
                (g.i_c, g.world_a.b_R) = (armoury, 0);
                g.world_a.i_u = AiSamples.Computer;
                g.world_a.aq_();
            }, s =>
            {
                ClearQueues(s.World);
                (s.World.Frame, s.World.Ai.Flags) = (armoury, 0);
                s.ComputerPlayer.Step(AiSamples.Computer);
            });
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 40);
    }

    private static void ClearQueues(Tribes.Legacy.GameCanvas game)
    {
        foreach (sbyte[] queue in game.bArr3_a[AiSamples.Computer])
        {
            for (int p = 0; p < Pending; p++)
            {
                queue[p] = 0;
            }
        }
    }

    private static void ClearQueues(World.GameWorld world)
    {
        foreach (World.ArmouryOrders queue in world.Tribes[AiSamples.Computer].Armouries.Where(q => q is not null))
        {
            for (int p = 0; p < Pending; p++)
            {
                queue.SetPending(p, 0);
            }
        }
    }
}
