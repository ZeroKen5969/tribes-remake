using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// An endless world's part of each frame, at its very end: every few frames the land ahead of
/// the tribes is asked for, and on each frame the chunk due joins the world and tribes may come
/// into it when the people uncovered it; alliances with a tribe
/// that is out end; invitations that lapsed go; the presentation layer hears of the new land. Other worlds have no such part.
/// </summary>
internal sealed class EndlessUpdate
{
    private readonly GameWorld world;
    private readonly LandKeeper land;
    private readonly TribeSpawner spawner;
    private readonly Alliances alliances;

    /// <summary>Runs the endless part of <paramref name="world"/>'s frames.</summary>
    public EndlessUpdate(GameWorld world, LandKeeper land, TribeSpawner spawner, Alliances alliances)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.land = land ?? throw new ArgumentNullException(nameof(land));
        this.spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
        this.alliances = alliances ?? throw new ArgumentNullException(nameof(alliances));
    }

    /// <summary>One frame's endless part (nothing in other worlds).</summary>
    public void Advance()
    {
        if (world.Endless == null)
        {
            return;
        }
        if (world.Frame % world.Content.Endless!.Land.CheckEveryFrames == 0)
        {
            var byPeople = new HashSet<ChunkCoord>();
            foreach (ChunkCoord chunk in land.Missing(byPeople))
            {
                land.Order(chunk, byPeople.Contains(chunk));
            }
        }
        if (land.JoinDue() is { } uncovered)
        {
            spawner.Consider([uncovered]);
        }
        alliances.EndWithTribesOut();
        alliances.Lapse();
        land.Announce();
    }
}
