using System;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="NeedsChoices"/> with the original.</summary>
public sealed class NeedsChoicesTests
{
    private const int Happiness = 5, Satiety = 20, Energy = 21, MaxHitPoints = 67, Status = 79;

    /// <summary>Needs set before the check: energy, satiety, happiness, hit points as a fraction of the maximum (in tenths), sick.</summary>
    private static readonly (int Energy, int Satiety, int Happiness, int Health, bool Sick)[] Needs =
    {
        (90, 90, 90, 10, false), (10, 90, 80, 10, false), (40, 90, 3, 10, false), (90, 15, 80, 10, false),
        (90, 40, 2, 10, false), (90, 90, 20, 10, false), (90, 90, 4, 10, false), (90, 90, 80, 5, false),
        (90, 90, 80, 10, true),
    };

    private static readonly int[] Actions = { 69, 20, 59, 10, 66, 86 };

    private readonly ITestOutputHelper output;

    public NeedsChoicesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SeeToNeeds_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 6))
            {
                for (int v = 0; v < 3; v++)
                {
                    int k = id + sample.Frame + (v * 5);
                    var need = Needs[k % Needs.Length];
                    int action = Actions[k % Actions.Length];
                    Action<GameCanvas> prepare = g =>
                    {
                        Set(g, ActionField, id, action);
                        Set(g, Energy, id, need.Energy);
                        Set(g, Satiety, id, need.Satiety);
                        Set(g, Happiness, id, need.Happiness);
                        Set(g, HitPoints, id, Math.Max(1, Get(g, MaxHitPoints, id) * need.Health / 10));
                        Set(g, Status, id, need.Sick ? Get(g, Status, id) | 4 : Get(g, Status, id) & ~4);
                    };
                    run.Check(sample, $"SeeToNeeds unit {id} need {k % Needs.Length} action {action}", prepare,
                        g => { BeginTurn(g, id); return g.world_a.n_(); },
                        s => s.NeedsChoices.SeeToNeeds(TargetCycleTests.Turn(s, id)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }
}
