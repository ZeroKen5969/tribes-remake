using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>
/// The tribes' camps of a world by place, so the questions "which camps are near this cell" cost
/// the same however many tribes there are. A camp is set when its tribe settles and never moves:
/// a world that starts tells the index each camp as it settles (<see cref="Starting"/>,
/// <see cref="Settled"/>), so tribes not settled yet are not in it; any other world (loaded, or made
/// some other way) is indexed whole when first asked.
/// </summary>
internal sealed class CampIndex
{
    /// <summary>Buckets are this many cells square (as a power of two).</summary>
    private const int BucketShift = 6;

    private static readonly ConditionalWeakTable<GameWorld, CampIndex> Indexes = new();

    private readonly Dictionary<(int X, int Y), List<int>> buckets = new();
    private readonly Dictionary<int, (int X, int Y)> keys = new();
    private bool built;

    /// <summary>The index of <paramref name="world"/>'s camps as they are now.</summary>
    public static CampIndex For(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        CampIndex index = Indexes.GetValue(world, _ => new CampIndex());
        if (!index.built)
        {
            index.built = true;
            foreach (TribeState tribe in world.Tribes)
            {
                index.Put(tribe.Index, tribe.Camp);
            }
        }
        return index;
    }

    /// <summary><paramref name="world"/> starts: no camp is settled yet (tribes are told as they settle).</summary>
    public static void Starting(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        Indexes.AddOrUpdate(world, new CampIndex { built = true });
    }

    /// <summary>The camp of <paramref name="tribe"/> was set (it settled).</summary>
    public static void Settled(GameWorld world, int tribe)
    {
        CampIndex index = For(world);
        index.Put(tribe, world.Tribes[tribe].Camp);
    }

    /// <summary>
    /// Calls <paramref name="visit"/> with every tribe whose camp may lie within
    /// <paramref name="reach"/> cells of <paramref name="cell"/> (a few farther ones too); stops and
    /// returns false when it returns false.
    /// </summary>
    public bool Near<TState>(GridPoint cell, int reach, TState state, Func<TState, int, bool> visit)
    {
        ArgumentNullException.ThrowIfNull(visit);
        for (int by = (cell.Y - reach) >> BucketShift; by <= (cell.Y + reach) >> BucketShift; by++)
        {
            for (int bx = (cell.X - reach) >> BucketShift; bx <= (cell.X + reach) >> BucketShift; bx++)
            {
                if (!buckets.TryGetValue((bx, by), out List<int>? list))
                {
                    continue;
                }
                foreach (int tribe in list)
                {
                    if (!visit(state, tribe))
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    private void Put(int tribe, GridPoint camp)
    {
        if (keys.TryGetValue(tribe, out (int X, int Y) old))
        {
            buckets[old].Remove(tribe);
        }
        (int X, int Y) key = (camp.X >> BucketShift, camp.Y >> BucketShift);
        if (!buckets.TryGetValue(key, out List<int>? list))
        {
            buckets[key] = list = new List<int>();
        }
        list.Add(tribe);
        keys[tribe] = key;
    }
}
