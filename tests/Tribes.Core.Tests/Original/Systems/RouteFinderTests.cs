using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Paths;
using Tribes.Core.World;
using Tribes.Legacy;
using LegacyWorld = Tribes.Legacy.World;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="RouteFinder"/> (route segments, waypoints, route requests) with the original, plus edge cases.</summary>
public sealed class RouteFinderTests
{
    private const int UnitsPerSample = 8;
    private const int AnimalsPerSample = 5;
    private const int RangeTable = 38;
    private const int Offset = 30;

    private readonly ITestOutputHelper output;

    public RouteFinderTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void PlanSegment_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach ((int id, GridPoint goal) in Journeys(game))
            {
                foreach (int offset in new[] { 0, Offset })
                {
                    run.Check(sample, $"PlanSegment unit {id} to {goal} offset {offset}",
                        g =>
                        {
                            PrepareUnit(g, id);
                            GridPoint from = UnitCell(g, id);
                            int length = g.world_a.a_IIIIII(from.Y, from.X, goal.Y, goal.X, offset, 0);
                            return Original(g, length);
                        },
                        s =>
                        {
                            Unit unit = s.World.Unit(id);
                            var search = s.Routes.PlanSegment(unit, new GridPoint(unit.X, unit.Y), goal, offset);
                            return $"{search.Length},{search.Completed}";
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 600);
    }

    [Fact]
    public void PlanWaypoints_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            foreach ((int id, GridPoint goal) in Journeys(sample.Restore()))
            {
                run.Check(sample, $"PlanWaypoints unit {id} to {goal}",
                    g =>
                    {
                        PrepareUnit(g, id);
                        GridPoint from = UnitCell(g, id);
                        int count = g.world_a.a_IIII(from.Y, from.X, goal.Y, goal.X);
                        return string.Join(";", Enumerable.Range(0, count + 1).Select(i => $"{g.world_a.bArr2_d[0][i]},{g.world_a.bArr2_d[1][i]}"));
                    },
                    s =>
                    {
                        Unit unit = s.World.Unit(id);
                        var waypoints = new GridPoint[RouteSearch.MaxObstacles + 2];
                        int count = s.Routes.PlanWaypoints(unit, new GridPoint(unit.X, unit.Y), goal, waypoints);
                        return string.Join(";", waypoints.Take(count + 1).Select(p => $"{p.X},{p.Y}"));
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void RequestRoute_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            foreach ((int id, GridPoint goal) in Journeys(sample.Restore()))
            {
                run.Check(sample, $"RequestRoute unit {id} to {goal}",
                    g =>
                    {
                        PrepareUnit(g, id);
                        g.bArr_e[(UnitDestinationX * 101) + id] = (sbyte)goal.X;
                        g.bArr_e[(UnitDestinationY * 101) + id] = (sbyte)goal.Y;
                        g.world_a.o_();
                        return !g.world_a.zArr_a[2];
                    },
                    s =>
                    {
                        Unit unit = s.World.Unit(id);
                        (unit.DestinationX, unit.DestinationY) = (goal.X, goal.Y);
                        return s.Routes.RequestRoute(unit).Surrounded;
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void PlanAnimalRoute_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All)
        {
            var game = sample.Restore();
            foreach (int index in SampleInputs.Spread(LiveAnimals(game), AnimalsPerSample))
            {
                int range = index % 3;
                GridPoint goal = SampleInputs.Spread(LegacySamples.LiveUnits(game), AnimalsPerSample)
                    .Select(id => UnitCell(game, id)).Skip(index % AnimalsPerSample).FirstOrDefault();
                run.Check(sample, $"PlanAnimalRoute animal {index} to {goal}",
                    g =>
                    {
                        LegacyWorld w = g.world_a;
                        (w.i_v, w.i_B, w.zArr_a[2]) = (index, range, true);
                        g.bArr_e[Parity.EntityFields.AnimalBase + (AnimalDestinationX * 22) + index] = (sbyte)goal.X;
                        g.bArr_e[Parity.EntityFields.AnimalBase + (AnimalDestinationY * 22) + index] = (sbyte)goal.Y;
                        int length = w.a_IIIIII(AnimalField(g, AnimalY, index), AnimalField(g, AnimalX, index), goal.Y, goal.X, 0, 101);
                        return $"{length},{!w.zArr_a[2]}";
                    },
                    s =>
                    {
                        Animal animal = s.World.Animal(index);
                        (animal.DestinationX, animal.DestinationY) = (goal.X, goal.Y);
                        RouteResult route = s.Routes.PlanAnimalRoute(animal, range);
                        return $"{route.Length},{route.Surrounded}";
                    });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 80);
    }

    [Fact]
    public void FindRoute_GoesRoundAWall()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        for (int y = 2; y <= 8; y++)
        {
            world.Map.SetCell(new GridPoint(10, y), MapCell.Of(CellContent.Impassable));
        }
        Unit unit = world.Unit(51);
        (unit.HitPoints, unit.X, unit.Y, unit.Age, unit.Action, unit.DestinationX, unit.DestinationY) = (10, 5, 5, 20, 69, 15, 5);
        world.Map.SetCell(new GridPoint(5, 5), MapCell.UnitAt(51));

        RouteResult route = systems.Routes.RequestRoute(unit);

        Assert.False(route.Surrounded);
        Assert.Equal(10, route.Length);
        GridPoint last = new(unit.Path.XAt(route.Length - 1), unit.Path.YAt(route.Length - 1));
        Assert.Equal(new GridPoint(15, 5), last);
        Assert.All(Enumerable.Range(0, route.Length), i =>
            Assert.True(world.Map.CellAt(new GridPoint(unit.Path.XAt(i), unit.Path.YAt(i))).IsEmpty));
    }

    [Fact]
    public void FindRoute_InABox_EndsNearTheGoal()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        foreach (GridPoint wall in Ring(new GridPoint(5, 5), 1))
        {
            world.Map.SetCell(wall, MapCell.Of(CellContent.Impassable));
        }
        Unit unit = world.Unit(51);
        (unit.HitPoints, unit.X, unit.Y, unit.Age, unit.Action, unit.DestinationX, unit.DestinationY) = (10, 5, 5, 20, 69, 20, 5);

        RouteResult route = systems.Routes.RequestRoute(unit);

        Assert.True(route.Surrounded);
        Assert.Equal(0, route.Length);
    }

    [Fact]
    public void FindRoute_StopsCountingObstaclesAtTen()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        for (int x = 3; x <= 35; x += 3)
        {
            world.Map.SetCell(new GridPoint(x, 10), MapCell.Of(CellContent.Impassable));
        }
        Unit unit = world.Unit(51);
        (unit.HitPoints, unit.X, unit.Y, unit.Age, unit.Action, unit.DestinationX, unit.DestinationY) = (10, 1, 10, 20, 69, 37, 10);

        RouteResult route = systems.Routes.RequestRoute(unit);

        Assert.Equal(36, route.Length);
        Assert.Equal(new GridPoint(37, 10), new GridPoint(unit.Path.XAt(route.Length - 1), unit.Path.YAt(route.Length - 1)));
    }

    [Fact]
    public void FindRoute_InTheShortRouteMission_KeepsTwoWaypoints()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        world.Mission = 3;
        Unit unit = world.Unit(51);
        (unit.HitPoints, unit.X, unit.Y, unit.Age, unit.Action, unit.DestinationX, unit.DestinationY) = (10, 2, 2, 20, 69, 12, 2);

        Assert.Equal(2, systems.Routes.RequestRoute(unit).Length);
        Assert.Equal(2, unit.PathLength);
    }

    private static IEnumerable<GridPoint> Ring(GridPoint centre, int radius) =>
        from dy in Enumerable.Range(-radius, (2 * radius) + 1)
        from dx in Enumerable.Range(-radius, (2 * radius) + 1)
        where dx != 0 || dy != 0
        select new GridPoint(centre.X + dx, centre.Y + dy);

    /// <summary>Units on their cells, each sent to a building or another unit of the sample.</summary>
    private static IEnumerable<(int Unit, GridPoint Goal)> Journeys(GameCanvas game)
    {
        int[] units = LegacySamples.LiveUnits(game).Where(id => CodeAt(game, UnitCell(game, id).X, UnitCell(game, id).Y) == id).ToArray();
        GridPoint[] goals = LegacySamples.UsedBuildings(game)
            .Select(b => new GridPoint(LegacySamples.BuildingField(game, 0, b), LegacySamples.BuildingField(game, 1, b)))
            .Concat(units.Select(id => UnitCell(game, id))).ToArray();
        return SampleInputs.Spread(units, UnitsPerSample).Select((id, i) => (id, goals[(i * 7 + id) % goals.Length]));
    }

    /// <summary>Sets the original's registers as its unit loop has them when it plans for <paramref name="id"/>.</summary>
    private static void PrepareUnit(GameCanvas g, int id)
    {
        LegacyWorld w = g.world_a;
        int range = w.bArr_b[w.sArr_a[RangeTable] + LegacySamples.Field(g, UnitAction, id)];
        if (range < 0)
        {
            range = g.bArr_e[w.sArr_b[-range] + id];
        }
        (w.i_v, w.i_u, w.i_B, w.zArr_a[2]) = (id, TribeOf(id), range, true);
    }

    private static string Original(GameCanvas g, int length)
    {
        LegacyWorld w = g.world_a;
        bool completed = w.b_c == 0 || w.zArr_a[0] || w.zArr_a[1];
        return $"{length},{completed}";
    }
}
