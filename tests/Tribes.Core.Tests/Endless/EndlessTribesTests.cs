using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Endless;

public sealed class EndlessTribesTests
{
    private readonly ITestOutputHelper output;

    public EndlessTribesTests(ITestOutputHelper output) => this.output = output;

    /// <summary>The controller of the n-th tribe added: mostly computers, now and then a person who is away.</summary>
    private static TribeController Newcomer(int n) => n % 10 == 3 ? TribeController.Absent(4 + n) : TribeController.Computer(n % 2);

    [Fact]
    [Trait("Speed", "Long")]
    public void AddTribe_TwoHundredTribesAllGetCampsAndPeersStayAlike()
    {
        GameSimulation a = EndlessWorlds.Start(humans: 1, computers: 0, seed: 77);
        GameSimulation b = EndlessWorlds.Start(humans: 1, computers: 0, seed: 77);
        for (int n = 0; n < 200; n++)
        {
            int added = a.AddTribe(Newcomer(n));
            Assert.Equal(n + 1, added);
            Assert.Equal(added, b.AddTribe(Newcomer(n)));
            if (n % 25 == 24)
            {
                for (int tick = 0; tick < 20; tick++)
                {
                    a.Step();
                    b.Step();
                }
                Assert.Equal(a.StateHash(), b.StateHash());
            }
        }
        GameWorld world = a.World;
        Assert.Equal(201, world.Tribes.Count);
        AssertCampsApartAndJoined(world);
        output.WriteLine($"{world.Map.Chunks.Count} chunks, bounds {world.Map.Bounds}");

        GameSimulation loaded = GameSimulation.Load(EndlessWorlds.Content, a.Save());
        Assert.Equal(a.StateHash(), loaded.StateHash());
        for (int tick = 0; tick < 50; tick++)
        {
            a.Step();
            loaded.Step();
            b.Step();
        }
        Assert.Equal(a.StateHash(), loaded.StateHash());
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void AddTribe_GivesAJoiningPersonACampAndAnnouncesIt()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 3, seed: 2);

        int tribe = game.AddTribe(TribeController.Human(1));
        TickResult tick = game.Step();

        Assert.Equal(4, tribe);
        Assert.Contains(tick.Events.OfType<TribeAppeared>(), e => e.Tribe == tribe && !e.Computer);
        Assert.True(game.World.Tribes[tribe][TribeCounter.Chief] > 0);
        Assert.Equal(game.World.Frame - 1, game.World.Tribes[tribe].ArrivedAtFrame);
    }

    [Fact]
    public void NewTribes_AppearInLandThePeopleUncover()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 0, seed: 8, newTribes: 3);
        GameWorld world = game.World;
        game.Step();
        int before = world.Tribes.Count;
        int explorer = world.UnitsOf(0).Select(u => u.Id).First(id => world.Unit(id).InUse);
        var appeared = new List<TribeAppeared>();
        for (int leg = 1; leg <= 24 && appeared.Count == 0; leg++)
        {
            // Walk the explorer further and further east, as a player would.
            var goal = new GridPoint(world.Tribes[0].Camp.X + (leg * 40), world.Tribes[0].Camp.Y);
            appeared.AddRange(game.Step(TickInput.WithOrders(new MoveUnits(0, new UnitSet(new[] { explorer }), goal, 0, true)))
                .Events.OfType<TribeAppeared>());
            for (int tick = 0; tick < 400; tick++)
            {
                appeared.AddRange(game.Step().Events.OfType<TribeAppeared>());
            }
        }
        Assert.NotEmpty(appeared);
        Assert.True(world.Tribes.Count > before);
        TribeAppeared first = appeared[0];
        Assert.True(first.Computer);
        Assert.Equal(world.Difficulty, world.Tribes[first.Tribe].Controller.Difficulty ?? world.Difficulty);
        Assert.True(world.Tribes[first.Tribe].ArrivedAtFrame > 0);
    }

    [Fact]
    public void NewTribes_NoneWhenTheDensityIsZero()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 0, seed: 8, newTribes: 0);

        game.Step();
        game.GenerateLand(new GridRect(-512, -512, 1024, 1024));
        for (int tick = 0; tick < 100; tick++)
        {
            Assert.Empty(game.Step().Events.OfType<TribeAppeared>());
        }
        Assert.Single(game.World.Tribes);
    }

    private static void AssertCampsApartAndJoined(GameWorld world)
    {
        int minimum = world.Content.Endless!.Camps.MinimumDistance;
        var camps = world.Tribes.Select(t => t.Camp).ToArray();
        for (int i = 0; i < camps.Length; i++)
        {
            for (int j = i + 1; j < camps.Length; j++)
            {
                long dx = camps[i].X - camps[j].X, dy = camps[i].Y - camps[j].Y;
                Assert.True((dx * dx) + (dy * dy) >= (long)minimum * minimum, $"camps {i} and {j} too close");
            }
        }
        HashSet<GridPoint> reached = Flood(world, NearestOpen(world, camps[0]));
        foreach (GridPoint camp in camps)
        {
            Assert.Contains(NearestOpen(world, camp), reached);
        }
    }

    /// <summary>An open cell next to the camp's site (where its units walk out).</summary>
    private static GridPoint NearestOpen(GameWorld world, GridPoint camp)
    {
        for (int ring = 1; ring < 20; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    var cell = new GridPoint(camp.X + dx, camp.Y + dy);
                    if (Walkable(world, cell))
                    {
                        return cell;
                    }
                }
            }
        }
        throw new InvalidOperationException($"camp {camp} has no open cell nearby");
    }

    private static bool Walkable(GameWorld world, GridPoint cell) =>
        world.Map.InPlay(cell) && world.Map.CellAt(cell).Content is CellContent.Empty or CellContent.Unit or CellContent.Animal or CellContent.Reserved;

    private static HashSet<GridPoint> Flood(GameWorld world, GridPoint start)
    {
        var reached = new HashSet<GridPoint> { start };
        var queue = new Queue<GridPoint>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            GridPoint cell = queue.Dequeue();
            foreach (GridPoint next in new[]
            {
                new GridPoint(cell.X + 1, cell.Y), new GridPoint(cell.X - 1, cell.Y),
                new GridPoint(cell.X, cell.Y + 1), new GridPoint(cell.X, cell.Y - 1),
            })
            {
                if (Walkable(world, next) && reached.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }
        return reached;
    }
}
