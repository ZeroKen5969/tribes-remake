using System.Linq;
using Tribes.Core.Systems.Units.Work;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="NearestBuildings"/> with the original.</summary>
public sealed class NearestBuildingsTests
{
    private static readonly int[] Kinds = Enumerable.Range(0, 12).ToArray();
    private static readonly int[] Hints = { 0, 2, 3 };

    private readonly ITestOutputHelper output;

    public NearestBuildingsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void HeadForAndGoFor_MatchTheOriginal()
    {
        var run = new UnitWorkParity();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 4))
            {
                foreach (int kind in SampleInputs.Pick(Kinds, id + sample.Frame, 2).Append(6))
                {
                    int k = kind;
                    int action = (id + kind) % 3 == 0 ? 20 : 69;
                    run.Check(sample, $"HeadFor unit {id} kind {k}", g => Set(g, ActionField, id, action),
                        g => { BeginTurn(g, id); return g.world_a.a_I_Z(k); },
                        s => s.NearestBuildings.HeadFor(s.World.Unit(id), k));
                    int hint = Hints[k % Hints.Length];
                    run.Check(sample, $"GoFor unit {id} kind {k}", null,
                        g => { BeginTurn(g, id); return g.world_a.a_IB_Z(k, (sbyte)hint); },
                        s => s.NearestBuildings.GoFor(s.World.Unit(id), k, hint));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void GoFor_WithoutABuildingOfTheKindStaysPut()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        Tribes.Core.World.Unit unit = systems.World.Unit(1);
        (unit.X, unit.Y, unit.DestinationX, unit.DestinationY) = (4, 5, 9, 9);
        Assert.False(systems.NearestBuildings.GoFor(unit, 4, 0));
        Assert.Equal((4, 5), (unit.DestinationX, unit.DestinationY));
    }

    [Fact]
    public void HeadFor_PrefersAStoreToAStoreSite()
    {
        var systems = new Simulation.RuleSystems(TestWorlds.Empty());
        Tribes.Core.World.Building site = systems.World.Building(1), store = systems.World.Building(2);
        (site.HitPoints, site.Kind, site.Aux, site.Level, site.X, site.Y, site.OperatorOrBeds) = (5, 16, 6, 3, 1, 1, 1);
        (store.HitPoints, store.Kind, store.Level, store.X, store.Y, store.OperatorOrBeds) = (50, 6, 1, 20, 20, 1);
        Tribes.Core.World.Unit unit = systems.World.Unit(1);
        (unit.X, unit.Y, unit.Action) = (2, 2, 20);
        Assert.True(systems.NearestBuildings.HeadFor(unit, 6));
        Assert.Equal((20, 20), (unit.DestinationX, unit.DestinationY));
    }
}
