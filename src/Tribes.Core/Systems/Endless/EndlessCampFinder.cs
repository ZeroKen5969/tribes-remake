using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// The camp finder of endless worlds: it never runs out of room. It looks through the land made,
/// ring by ring from the start, for the first chunk with a camp site at least the camps' distance
/// from every tribe's camp and from where each tribe is now; when there is none it makes land at
/// the frontier, chunk by chunk, until one has room. The site is cleared before it is given.
/// </summary>
public sealed class EndlessCampFinder : ICampFinder
{
    /// <summary>Chunks made at the frontier without a site before one is cleared by force (land as the generator makes it always has room long before).</summary>
    private const int FrontierTries = 64;

    private readonly LandKeeper land;
    private readonly CampSites sites;

    /// <summary>While a world starts: the chunks found without room (camps are only added then, so they stay without).</summary>
    private HashSet<ChunkCoord>? full;

    internal EndlessCampFinder(LandKeeper land, CampSites sites)
    {
        this.land = land;
        this.sites = sites;
    }

    /// <inheritdoc/>
    public GridPoint? FindCamp(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var keepAway = new CampDistances(world.Content.Endless!.Camps.MinimumDistance);
        foreach (TribeState tribe in world.Tribes)
        {
            keepAway.Add(tribe.Camp);
            keepAway.Add(TribeStanding.Seat(world, tribe.Index));
        }
        return Find(world, keepAway);
    }

    /// <summary>
    /// The start of a world begins (true) or ends: in between, a chunk found without room is not
    /// looked at again (the start only adds camps, so it stays without room).
    /// </summary>
    internal void Starting(bool starting) => full = starting ? new HashSet<ChunkCoord>() : null;

    /// <summary>A cleared camp site allowed by <paramref name="keepAway"/>, making land as needed.</summary>
    internal GridPoint Find(GameWorld world, CampDistances keepAway)
    {
        foreach (ChunkCoord chunk in LandKeeper.RingOrder(world.Map.Chunks.Select(c => c.Coord)).ToArray())
        {
            if (full?.Contains(chunk) == true)
            {
                continue;
            }
            if (sites.FindIn(chunk, keepAway) is { } site)
            {
                sites.Clear(site);
                return site;
            }
            full?.Add(chunk);
        }
        for (int i = 0; ; i++)
        {
            ChunkCoord next = land.Frontier();
            land.Make(next);
            if (sites.FindIn(next, keepAway) is { } site)
            {
                sites.Clear(site);
                return site;
            }
            full?.Add(next);
            if (i >= FrontierTries && sites.ForceIn(next) is { } forced)
            {
                return forced;
            }
        }
    }
}
