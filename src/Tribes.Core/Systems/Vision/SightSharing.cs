using System;
using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Vision;

/// <summary>
/// Who shares sight with whom: in a match a tribe sees what its allies see (team mates and tribes
/// allied during the game); in the campaign nobody shares. The lists are worked out again whenever
/// the relations or the number of tribes change, so asking costs nothing in between.
/// </summary>
internal sealed class SightSharing
{
    private readonly GameWorld world;
    private int[]?[] allies = Array.Empty<int[]?>();
    private TribeRelations? relations;
    private int version = -1;

    /// <summary>Shares the sight of <paramref name="world"/>'s tribes.</summary>
    public SightSharing(GameWorld world) => this.world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>The other tribes whose sight <paramref name="tribe"/> shares, in tribe order (none in the campaign).</summary>
    public int[] AlliesOf(int tribe)
    {
        if (world.Mode != WorldMode.Match || tribe < 0 || tribe >= world.Tribes.Count)
        {
            return Array.Empty<int>();
        }
        if (!ReferenceEquals(relations, world.Relations) || version != world.Relations.Version || allies.Length != world.Tribes.Count)
        {
            relations = world.Relations;
            version = relations.Version;
            allies = new int[]?[world.Tribes.Count];
        }
        return allies[tribe] ??= Find(tribe);
    }

    private int[] Find(int tribe)
    {
        var found = new SortedSet<int>();
        int team = world.Tribes[tribe].Team;
        if (team >= 0)
        {
            foreach (TribeState other in world.Tribes)
            {
                if (other.Index != tribe && other.Team == team)
                {
                    found.Add(other.Index);
                }
            }
        }
        foreach ((int low, int high) in world.Relations.Alliances)
        {
            if (low == tribe || high == tribe)
            {
                found.Add(low == tribe ? high : low);
            }
        }
        return found.Count == 0 ? Array.Empty<int>() : [.. found];
    }
}
