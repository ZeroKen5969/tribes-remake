using System;
using System.Linq;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;
using static Tribes.Core.Tests.Systems.WorkScenarios;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="ArmouryDeliveries"/> with the original.</summary>
public sealed class ArmouryDeliveriesTests
{
    private const int Products = 9, FirstTrapProduct = 6, TrapKindOffset = 14;

    private readonly ITestOutputHelper output;

    public ArmouryDeliveriesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void PickUpAndDeliver_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 8))
            {
                if (FindBuilding(sample.Restore(), TribeOf(id), Armoury) == 0)
                {
                    continue;
                }
                int product = (id + sample.Frame) % Products;
                int action = product >= FirstTrapProduct ? 27 : 26;
                Action<GameCanvas> prepare = g => Worker(g, id, product, action);
                run.Check(sample, $"PickUp unit {id} product {product}", prepare,
                    g => { BeginTurn(g, id); g.world_a.f_Z(false); },
                    s => s.ArmouryDeliveries.PickUp(s.World.Unit(id)));
                run.Check(sample, $"Deliver unit {id} product {product}", prepare,
                    g => { BeginTurn(g, id); g.world_a.f_Z(true); },
                    s => s.ArmouryDeliveries.Deliver(s.World.Unit(id)));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    /// <summary>
    /// <paramref name="id"/> carries <paramref name="product"/> of its armoury; a trap has its order
    /// at a free cell next to the unit, which is its destination.
    /// </summary>
    private static void Worker(GameCanvas g, int id, int product, int action)
    {
        int armoury = ArmouryWorker(g, id, product, 2);
        Set(g, ActionField, id, action);
        if (product < FirstTrapProduct)
        {
            return;
        }
        int x = Get(g, X, id), y = Get(g, Y, id);
        (int fx, int fy) = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Select(d => (x + d.Item1, y + d.Item2))
            .FirstOrDefault(c => c.Item1 >= 0 && c.Item2 >= 0 && c.Item1 < g.s_c && c.Item2 < g.s_d && g.bArr2_c[c.Item2][c.Item1] == 0, (x, y));
        int slot = Enumerable.Range(0, 30).First(i => g.bArr2_f[0][i] == 0 || i == 29);
        (g.bArr2_f[0][slot], g.bArr2_f[1][slot], g.bArr2_f[2][slot], g.bArr2_f[3][slot]) =
            ((sbyte)armoury, (sbyte)(product + TrapKindOffset), (sbyte)fx, (sbyte)fy);
        Set(g, DestinationX, id, fx);
        Set(g, DestinationY, id, fy);
    }
}
