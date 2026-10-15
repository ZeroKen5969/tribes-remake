using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Maps.Generation;
using Tribes.Core.Simulation;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// Makes the land of an endless world as it is needed: around the start, ahead of every unit and
/// building (so no tribe ever reaches land not made), and outward at the frontier when a camp needs
/// room. Chunks needed at the same time are made in rows from the top, each row from the left, so
/// every peer makes the same land. Land ahead of the tribes is made off the frame
/// (<see cref="LandWorks"/>) and joins the world a little later; the start, a camp at the frontier
/// and <see cref="EnsureArea"/> make it at once (after the chunks being made have joined).
/// </summary>
internal sealed class LandKeeper
{
    private readonly GameWorld world;
    private readonly Action<GameEvent> raise;
    private readonly Action<ChunkCoord> onMade;
    private readonly List<ChunkCoord> made = new();
    private readonly LandWorks works;
    private TerrainGenerator? generator;

    /// <summary>Makes the land of <paramref name="world"/>; <paramref name="onMade"/> hears of every chunk made.</summary>
    public LandKeeper(GameWorld world, Action<GameEvent> raise, Action<ChunkCoord> onMade)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
        this.onMade = onMade ?? throw new ArgumentNullException(nameof(onMade));
        works = new LandWorks(world);
    }

    private EndlessRules Rules => world.Content.Endless!;

    private TerrainGenerator Generator => generator ??= new TerrainGenerator(world.Content);

    /// <summary>The order in which chunks are visited when looking for room: ring by ring from chunk (0, 0).</summary>
    public static IOrderedEnumerable<ChunkCoord> RingOrder(IEnumerable<ChunkCoord> chunks) => chunks
        .OrderBy(c => Math.Max(Math.Abs(c.X), Math.Abs(c.Y)))
        .ThenBy(c => Math.Abs(c.X) + Math.Abs(c.Y))
        .ThenBy(c => c.Y)
        .ThenBy(c => c.X);

    /// <summary>Makes every chunk touching <paramref name="area"/> that is not made yet; returns them.</summary>
    public List<ChunkCoord> EnsureArea(GridRect area)
    {
        var wanted = new List<ChunkCoord>();
        if (area.IsEmpty)
        {
            return wanted;
        }
        for (int cy = area.Top >> WorldMap.ChunkShift; cy <= (area.Bottom - 1) >> WorldMap.ChunkShift; cy++)
        {
            for (int cx = area.Left >> WorldMap.ChunkShift; cx <= (area.Right - 1) >> WorldMap.ChunkShift; cx++)
            {
                var chunk = new ChunkCoord(cx, cy);
                if (world.Map.Chunk(chunk) == null)
                {
                    wanted.Add(chunk);
                }
            }
        }
        foreach (ChunkCoord chunk in wanted)
        {
            Make(chunk);
        }
        return wanted;
    }

    /// <summary>
    /// The chunks within reach of a unit or building of any tribe that are neither made nor being
    /// made, in the order they are made; <paramref name="byPeople"/> gets those a human-ruled
    /// tribe's units or buildings are near.
    /// </summary>
    public List<ChunkCoord> Missing(HashSet<ChunkCoord> byPeople)
    {
        ArgumentNullException.ThrowIfNull(byPeople);
        var missing = new HashSet<ChunkCoord>();
        int reach = Rules.Land.ReachCells;
        foreach (TribeState tribe in world.Tribes)
        {
            HashSet<ChunkCoord>? people = tribe.Controller.IsHumanRuled ? byPeople : null;
            foreach (Unit unit in world.UnitsOf(tribe.Index))
            {
                if (unit.InUse)
                {
                    AddAround(new GridPoint(unit.X, unit.Y), reach, missing, people);
                }
            }
            foreach (Building building in world.BuildingsOf(tribe.Index))
            {
                if (building.InUse)
                {
                    AddAround(new GridPoint(building.X, building.Y), reach, missing, people);
                }
            }
        }
        return missing.OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
    }

    /// <summary>The chunk not made yet beside the land that comes first in <see cref="RingOrder"/> (chunk (0, 0) on an empty map).</summary>
    public ChunkCoord Frontier()
    {
        var candidates = new HashSet<ChunkCoord>();
        foreach (MapChunk chunk in world.Map.Chunks)
        {
            foreach (ChunkCoord next in new[]
            {
                new ChunkCoord(chunk.Coord.X + 1, chunk.Coord.Y), new ChunkCoord(chunk.Coord.X - 1, chunk.Coord.Y),
                new ChunkCoord(chunk.Coord.X, chunk.Coord.Y + 1), new ChunkCoord(chunk.Coord.X, chunk.Coord.Y - 1),
            })
            {
                if (world.Map.Chunk(next) == null)
                {
                    candidates.Add(next);
                }
            }
        }
        return candidates.Count == 0 ? new ChunkCoord(0, 0) : RingOrder(candidates).First();
    }

    /// <summary>Makes chunk <paramref name="chunk"/> now (the chunks being made join the world first; it may be one of them).</summary>
    public void Make(ChunkCoord chunk)
    {
        while (works.Busy)
        {
            Joined(works.Join(Generator).Coord);
        }
        if (world.Map.Chunk(chunk) != null)
        {
            return;
        }
        Generator.Generate(world.Map, chunk, world.Endless!.Seed);
        Joined(chunk);
    }

    /// <summary>Asks for <paramref name="chunk"/> to be made off the frame (see <see cref="LandWorks.Order"/>).</summary>
    public void Order(ChunkCoord chunk, bool byPeople) => works.Order(chunk, byPeople);

    /// <summary>The chunk being made that is due joins the world (at most one a frame); returns it when the people asked for it.</summary>
    public ChunkCoord? JoinDue()
    {
        if (works.Due() is null)
        {
            return null;
        }
        LandOrder order = works.Join(Generator);
        Joined(order.Coord);
        return order.ByPeople ? order.Coord : null;
    }

    private void Joined(ChunkCoord chunk)
    {
        made.Add(chunk);
        onMade(chunk);
    }

    /// <summary>Tells the presentation layer of the chunks made since the last time.</summary>
    public void Announce()
    {
        if (made.Count > 0)
        {
            raise(new ChunksGenerated(made.ToArray()));
            made.Clear();
        }
    }

    private void AddAround(GridPoint at, int reach, HashSet<ChunkCoord> missing, HashSet<ChunkCoord>? people)
    {
        for (int cy = (at.Y - reach) >> WorldMap.ChunkShift; cy <= (at.Y + reach) >> WorldMap.ChunkShift; cy++)
        {
            for (int cx = (at.X - reach) >> WorldMap.ChunkShift; cx <= (at.X + reach) >> WorldMap.ChunkShift; cx++)
            {
                var chunk = new ChunkCoord(cx, cy);
                if (world.Map.Chunk(chunk) == null && !works.IsOrdered(chunk))
                {
                    missing.Add(chunk);
                    people?.Add(chunk);
                }
            }
        }
    }
}
