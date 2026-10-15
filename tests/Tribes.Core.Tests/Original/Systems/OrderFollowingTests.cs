using System;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="OrderFollowing"/> with the original.</summary>
public sealed class OrderFollowingTests
{
    private const int Happiness = 5, FirstAnimalCode = 101;

    private readonly ITestOutputHelper output;

    public OrderFollowingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Follow_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 6))
            {
                int enemy = EnemyOf(sample.Game, id);
                foreach (int mode in new[] { 2, 3, 4 })
                {
                    int m = mode, k = id + mode + sample.Frame;
                    bool unhappy = k % 3 == 0, animal = k % 2 == 0;
                    Action<GameCanvas> prepare = g =>
                    {
                        Set(g, OrderMode, id, m);
                        if (unhappy)
                        {
                            Set(g, Happiness, id, 5);
                        }
                        if (m == 4)
                        {
                            Set(g, TargetCellCode, id, animal ? FirstAnimalCode : Math.Max(enemy, 1));
                        }
                        if (m == 2 && enemy > 0 && k % 4 == 1)
                        {
                            Set(g, DestinationX, id, Get(g, X, enemy));
                            Set(g, DestinationY, id, Get(g, Y, enemy));
                        }
                    };
                    run.Check(sample, $"Follow unit {id} mode {m} unhappy {unhappy}", prepare,
                        g => { BeginTurn(g, id); g.world_a.t_(); },
                        s => s.OrderFollowing.Follow(s.World.Unit(id)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }
}
