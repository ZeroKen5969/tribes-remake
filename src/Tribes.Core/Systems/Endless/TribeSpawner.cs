using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Maps.Generation;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>A camp finder that gives one place it was told (a site already chosen and cleared).</summary>
internal sealed class ChosenCamp : ICampFinder
{
    private readonly GridPoint origin;

    public ChosenCamp(GridPoint origin) => this.origin = origin;

    public GridPoint? FindCamp(GameWorld world) => origin;
}

/// <summary>
/// Computer tribes coming into an endless world: each chunk the people's tribes uncover takes the
/// next computer tribe still waiting from the start (<see cref="EndlessState.WaitingTribes"/>);
/// once none waits, the chunk draws by the world's density setting whether a new tribe comes there.
/// Both play at the world's difficulty. A tribe comes when the chunk has a camp site far enough
/// from every camp and from the people's camps and units, and waits before it attacks
/// (<see cref="EndlessPacing"/>).
/// </summary>
internal sealed class TribeSpawner
{
    /// <summary>Mixed into the world seed for the draws of new tribes (apart from the land's).</summary>
    private const long SpawnSeedMix = 0x54524942; // "TRIB"

    private readonly GameWorld world;
    private readonly CampSites sites;
    private readonly Func<TribeController, ICampFinder, int> addTribe;
    private readonly Action<GameEvent> raise;

    /// <summary>Brings tribes into <paramref name="world"/> through <paramref name="addTribe"/>.</summary>
    public TribeSpawner(GameWorld world, CampSites sites, Func<TribeController, ICampFinder, int> addTribe, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.sites = sites ?? throw new ArgumentNullException(nameof(sites));
        this.addTribe = addTribe ?? throw new ArgumentNullException(nameof(addTribe));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
    }

    private EndlessNewTribeRules Rules => world.Content.Endless!.NewTribes;

    /// <summary>The people's tribes uncovered <paramref name="chunks"/> (just made): tribes may come there.</summary>
    public void Consider(IEnumerable<ChunkCoord> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        EndlessState state = world.Endless!;
        int density = Math.Clamp(state.NewTribes, 0, Rules.ChunksPerTribe.Count - 1);
        int chunksPerTribe = Rules.ChunksPerTribe[density];
        foreach (ChunkCoord chunk in chunks)
        {
            // A tribe the density draw brings waits like the start's that found no room: most chunks
            // have no ground for a whole camp, and the next chunk with room takes the first waiting.
            if (chunksPerTribe > 0 && Drawn(state, chunk, chunksPerTribe))
            {
                state.WaitingTribes++;
            }
            if (state.WaitingTribes > 0 && TrySpawn(chunk, TribeController.Computer()))
            {
                state.WaitingTribes--;
            }
        }
    }

    /// <summary>Whether the density draw of <paramref name="chunk"/> brings a new tribe.</summary>
    private static bool Drawn(EndlessState state, ChunkCoord chunk, int chunksPerTribe)
    {
        var random = new GameRandom(TerrainGenerator.ChunkSeed(state.Seed ^ SpawnSeedMix, chunk.X, chunk.Y));
        return random.NextMasked(int.MaxValue, chunksPerTribe) == 0;
    }

    /// <summary>A tribe played by <paramref name="controller"/> comes into <paramref name="chunk"/> when it has room; whether it came.</summary>
    private bool TrySpawn(ChunkCoord chunk, TribeController controller)
    {
        var camps = new CampDistances(world.Content.Endless!.Camps.MinimumDistance);
        var people = new CampDistances(Rules.DistanceFromPeople);
        var peoplesCamps = new CampDistances(Rules.DistanceFromPeopleCamps);
        foreach (TribeState tribe in world.Tribes)
        {
            camps.Add(tribe.Camp);
            camps.Add(TribeStanding.Seat(world, tribe.Index));
            if (tribe.Controller.IsHumanRuled)
            {
                peoplesCamps.Add(tribe.Camp);
                peoplesCamps.Add(TribeStanding.Seat(world, tribe.Index));
                AddPeople(people, tribe.Index);
            }
        }
        if (sites.FindIn(chunk, camps, people, peoplesCamps) is not { } site)
        {
            return false;
        }
        sites.Clear(site);
        int added = addTribe(controller, new ChosenCamp(site));
        if (added < 0)
        {
            return false;
        }
        raise(new TribeAppeared(added, site, Computer: true));
        return true;
    }

    private void AddPeople(CampDistances people, int tribe)
    {
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            if (unit.InUse)
            {
                people.Add(new GridPoint(unit.X, unit.Y));
            }
        }
    }
}
