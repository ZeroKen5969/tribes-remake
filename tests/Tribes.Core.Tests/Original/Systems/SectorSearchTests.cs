using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;
using Tribes.Legacy;
using LegacyWorld = Tribes.Legacy.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="SectorSearch"/> (sector index, target search, alarms) with the original, plus edge cases.</summary>
public sealed class SectorSearchTests
{
    private const int UnitsPerSample = 6;
    private const int SightField = 49;
    private const int AlarmRadius = 8;

    private readonly ITestOutputHelper output;

    public SectorSearchTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Rebuild_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            run.Check(sample, "Rebuild", g => g.world_a.n__V(), s => s.SectorSearch.Rebuild());
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 30);
    }

    [Fact]
    public void SearchGroup_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample))
            {
                GridPoint at = UnitCell(game, id);
                int sight = LegacySamples.Field(game, SightField, id);
                run.Check(sample, $"SearchGroup unit {id}", g => Original(g, id, at, sight), s => Rewritten(s, id, at, sight));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 150);
    }

    [Fact]
    public void RaiseAlarm_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), UnitsPerSample))
            {
                GridPoint at = UnitCell(game, id);
                int tribe = TribeOf(id);
                int code = id;
                run.Check(sample, $"RaiseAlarm at unit {id}",
                    g =>
                    {
                        g.world_a.b_D = (sbyte)code;
                        g.world_a.i_u = tribe;
                        g.world_a.a_BBB((sbyte)at.X, (sbyte)at.Y, AlarmRadius);
                    },
                    s => s.SectorSearch.RaiseAlarm(tribe, at, AlarmRadius, code));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 150);
    }

    [Fact]
    public void BeginSearch_KeepsTheSectorsOnTheMap()
    {
        TargetSearch search = SectorSearch.BeginSearch(new GridPoint(3, 90), 12);

        Assert.Equal((0, 0, 4, 5), (search.MinSectorX, search.MaxSectorX, search.MinSectorY, search.MaxSectorY));
        Assert.Equal(0, search.Priority);
    }

    [Fact]
    public void SearchGroup_PrefersPriorityThenDistance()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Unit searcher = world.Unit(1);
        (searcher.HitPoints, searcher.X, searcher.Y, searcher.SightRadius, searcher.Action) = (10, 5, 5, 9, 69);
        world.Map.SetCell(new GridPoint(5, 5), Maps.MapCell.UnitAt(1));
        foreach ((int id, int x, int age) in new[] { (51, 8, 20), (52, 6, 20), (53, 5, 5) })
        {
            Unit enemy = world.Unit(id);
            (enemy.HitPoints, enemy.X, enemy.Y, enemy.Age) = (10, x, 7, age);
            world.Map.SetCell(new GridPoint(x, 7), Maps.MapCell.UnitAt(id));
        }
        systems.SectorSearch.Rebuild();
        TargetSearch search = SectorSearch.BeginSearch(new GridPoint(5, 5), 9);

        systems.SectorSearch.SearchGroup(search, 1, searcher, new GridPoint(5, 5));

        Assert.Equal(52, search.Slot);
        Assert.Equal(4, search.Priority);
        Assert.Equal(new GridPoint(6, 7), search.Location);
    }

    [Fact]
    public void SearchGroup_SkipsTheDeadAndTheUnseen()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        Unit searcher = world.Unit(1);
        (searcher.HitPoints, searcher.X, searcher.Y, searcher.SightRadius, searcher.ThrowRange) = (10, 5, 5, 9, 4);
        foreach (int id in new[] { 51, 52 })
        {
            Unit enemy = world.Unit(id);
            (enemy.HitPoints, enemy.X, enemy.Y, enemy.Age) = (10, id - 45, 6, 20);
            world.Map.SetCell(new GridPoint(enemy.X, enemy.Y), Maps.MapCell.UnitAt(id));
        }
        systems.SectorSearch.Rebuild();
        world.Unit(51).HitPoints = -1;
        world.Unit(52).Status = UnitStatus.Undetected;
        TargetSearch search = SectorSearch.BeginSearch(new GridPoint(5, 5), 9);

        systems.SectorSearch.SearchGroup(search, 1, searcher, new GridPoint(5, 5));
        systems.SectorSearch.SearchGroup(search, 4, searcher, new GridPoint(5, 5));

        Assert.Equal(0, search.Priority);
    }

    private static string Original(GameCanvas g, int id, GridPoint at, int sight)
    {
        LegacyWorld w = g.world_a;
        w.i_v = id;
        w.b_BBB((sbyte)at.X, (sbyte)at.Y, (sbyte)sight);
        (w.b_y, w.b_z, w.b_A, w.b_F) = (0, 0, 0, 0);
        return string.Join(";", Enumerable.Range(0, 5).Select(group =>
        {
            w.i_aj = group;
            w.a_BBBB_V((sbyte)at.X, (sbyte)at.Y, 0, 0);
            return $"{w.b_y},{w.i_an},{w.b_z},{w.b_A},{w.b_F}";
        }));
    }

    private static string Rewritten(RuleSystems s, int id, GridPoint at, int sight)
    {
        TargetSearch search = SectorSearch.BeginSearch(at, sight);
        return string.Join(";", Enumerable.Range(0, 5).Select(group =>
        {
            s.SectorSearch.SearchGroup(search, group, s.World.Unit(id), at);
            return $"{search.Priority},{search.Distance},{search.Location.X},{search.Location.Y},{search.Slot}";
        }));
    }
}
