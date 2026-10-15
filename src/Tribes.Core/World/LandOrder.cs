using System;
using Tribes.Core.Maps;

namespace Tribes.Core.World;

/// <summary>
/// A chunk of an endless world that is being made: where, on which frame it joins the world,
/// whether the people's tribes asked for it (new tribes may come there), and the ring of its
/// design as it was when it was asked for (<see cref="Maps.Generation.ChunkCanvas.Ring"/> order):
/// tiles of chunks made already, <see cref="Open"/> for land not asked for, or
/// <see cref="FromOrder"/> for a chunk asked for earlier and not made yet (its design is used).
/// </summary>
internal sealed class LandOrder
{
    /// <summary>A ring cell of land not asked for.</summary>
    public const int Open = Maps.Generation.ChunkCanvas.Open;

    /// <summary>A ring cell of a chunk asked for earlier that has not joined the world yet.</summary>
    public const int FromOrder = -3;

    /// <summary>The highest tile number.</summary>
    private const int LastTile = byte.MaxValue;

    public LandOrder(ChunkCoord coord, int dueFrame, bool byPeople, int[] ring)
    {
        Coord = coord;
        DueFrame = dueFrame;
        ByPeople = byPeople;
        Ring = ring ?? throw new ArgumentNullException(nameof(ring));
    }

    /// <summary>The chunk.</summary>
    public ChunkCoord Coord { get; }

    /// <summary>The frame it joins the world on.</summary>
    public int DueFrame { get; }

    /// <summary>Whether a human-ruled tribe's units or buildings are near it.</summary>
    public bool ByPeople { get; }

    /// <summary>The ring of its design (see the class).</summary>
    public int[] Ring { get; }

    /// <summary>Saves, loads or hashes an order (<paramref name="order"/> is null when reading).</summary>
    /// <exception cref="System.IO.InvalidDataException">A loaded value is out of range.</exception>
    public static LandOrder Exchange(StateExchange s, LandOrder? order)
    {
        var coord = new ChunkCoord(s.Value(order?.Coord.X ?? 0), s.Value(order?.Coord.Y ?? 0));
        int due = s.Value(order?.DueFrame ?? 0);
        bool byPeople = s.Value(order?.ByPeople == true ? 1 : 0) != 0;
        int[] ring = order?.Ring ?? new int[Maps.Generation.ChunkCanvas.Ring.Count];
        for (int i = 0; i < ring.Length; i++)
        {
            ring[i] = s.Value(ring[i]);
            if (ring[i] is < FromOrder or > LastTile)
            {
                throw new System.IO.InvalidDataException($"land order {coord}: ring value {ring[i]}");
            }
        }
        return order ?? new LandOrder(coord, due, byPeople, ring);
    }
}
