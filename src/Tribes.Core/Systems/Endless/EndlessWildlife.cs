using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Maps.Generation;
using Tribes.Core.Systems.Match;
using Tribes.Core.Systems.Search;
using Tribes.Core.Systems.Wildlife;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// The wild animals of an endless world. Chunks get habitats when they are made, about as many per
/// area as the custom maps have (their kinds as one of the custom maps' habitats); animals are born
/// as on the custom maps, one birth for every two habitats in use, but only in habitats near a
/// tribe's camp still in the game or a unit of a human-ruled tribe, so land nobody is near stays
/// still.
/// </summary>
internal sealed class EndlessWildlife
{
    /// <summary>Mixed into the world seed for the draws that place habitats (apart from the land's).</summary>
    private const long HabitatSeedMix = 0x48414249; // "HABI"

    /// <summary>Random cells of a new chunk tried for a habitat's centre.</summary>
    private const int CentreTries = 16;

    /// <summary>The draws: chance and cells, habitat and kind (as the custom maps' births draw).</summary>
    private const int RollMask = 0xFFFFFF, Percent = 100;

    /// <summary>How far around the habitat's centre a free cell is looked for (spiral rounds).</summary>
    private const int BirthPlaceRounds = 10;

    /// <summary>Habitats in use per birth on a birth frame (a custom map has about two).</summary>
    private const int HabitatsPerBirth = 2;

    private readonly GameWorld world;
    private readonly SpiralSearch spiral;
    private readonly WildlifeAndNodes objects;

    /// <summary>The animals of <paramref name="world"/>.</summary>
    public EndlessWildlife(GameWorld world, SpiralSearch spiral, WildlifeAndNodes objects)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
        this.objects = objects ?? throw new ArgumentNullException(nameof(objects));
    }

    private EndlessWildlifeRules Rules => world.Content.Endless!.Wildlife;

    /// <summary>
    /// A chunk was made: by its own draws it gets habitats on open ground, on average
    /// <see cref="EndlessWildlifeRules.HabitatChancePercent"/> hundredths of one (each whole hundred
    /// one habitat, the rest the chance of one more).
    /// </summary>
    public void OnChunkMade(ChunkCoord chunk)
    {
        EndlessState state = world.Endless!;
        var random = new GameRandom(TerrainGenerator.ChunkSeed(state.Seed ^ HabitatSeedMix, chunk.X, chunk.Y));
        int count = (Rules.HabitatChancePercent / Percent)
            + (random.NextMasked(RollMask, Percent) < Rules.HabitatChancePercent % Percent ? 1 : 0);
        if (count == 0)
        {
            return;
        }
        List<(int Mission, int Point)> sources = Sources();
        if (sources.Count == 0)
        {
            return;
        }
        for (int habitat = 0; habitat < count; habitat++)
        {
            PlaceHabitat(chunk, random, sources);
        }
    }

    /// <summary>One habitat of a kind drawn from <paramref name="sources"/> on an empty cell of <paramref name="chunk"/>, if one is drawn.</summary>
    private void PlaceHabitat(ChunkCoord chunk, GameRandom random, List<(int Mission, int Point)> sources)
    {
        EndlessState state = world.Endless!;
        (int mission, int point) = sources[random.NextMasked(RollMask, sources.Count)];
        for (int i = 0; i < CentreTries; i++)
        {
            var cell = new GridPoint(chunk.Origin.X + random.NextMasked(RollMask, WorldMap.ChunkSize),
                chunk.Origin.Y + random.NextMasked(RollMask, WorldMap.ChunkSize));
            if (world.Map.CellAt(cell).IsEmpty)
            {
                state.AddHabitat(cell, mission, point);
                return;
            }
        }
    }

    /// <summary>
    /// On the birth frames of the world's mission, animals are born in random habitats in use (one
    /// birth for every two, as a custom map's two habitats get one), while a habitat has room.
    /// </summary>
    public void SpawnIfDue()
    {
        MissionRules mission = world.Content.Rules.Missions.Missions[world.Mission];
        if (!world.Settings.WildAnimals || world.Frame % (mission.AnimalSpawnInterval * AnimalSpawning.IntervalFrames) != AnimalSpawning.BirthPhase)
        {
            return;
        }
        List<int> active = ActiveHabitats();
        if (active.Count == 0)
        {
            return;
        }
        int births = Math.Max(1, active.Count / HabitatsPerBirth);
        for (int i = 0; i < births; i++)
        {
            int zone = active[world.Random.NextMasked(RollMask, active.Count)];
            AnimalHabitat habitat = world.Endless!.Habitats[zone];
            if (habitat.Animals >= Rules.AnimalsPerHabitat)
            {
                continue;
            }
            AnimalSpawnPoint source = world.Content.Rules.Missions.Missions[habitat.OddsMission].AnimalSpawnPoints[habitat.OddsPoint];
            if (AnimalSpawning.KindFor(world, source.KindCumulativePercent) is { } kind
                && CampLand.BirthPlace(world, spiral, habitat.Cell, BirthPlaceRounds) is { } cell)
            {
                objects.SpawnAnimal(kind, cell, zone);
            }
        }
    }

    /// <summary>The home of an animal of habitat <paramref name="zone"/>.</summary>
    public static GridPoint HomeOf(GameWorld world, int zone) => world.Endless!.Habitats[zone].Cell;

    /// <summary>
    /// The habitats in use: within reach of a camp of a tribe still in or of a unit of a human-ruled
    /// tribe still in (in the order they were made).
    /// </summary>
    private List<int> ActiveHabitats()
    {
        var near = new CampDistances(Rules.ActiveDistance);
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.OutAtFrame >= 0)
            {
                continue;
            }
            near.Add(TribeStanding.Seat(world, tribe.Index));
            if (!tribe.Controller.IsHumanRuled)
            {
                continue;
            }
            foreach (Unit unit in world.UnitsOf(tribe.Index))
            {
                if (unit.InUse)
                {
                    near.Add(new GridPoint(unit.X, unit.Y));
                }
            }
        }
        long reach = (long)Rules.ActiveDistance * Rules.ActiveDistance;
        var active = new List<int>();
        IReadOnlyList<AnimalHabitat> habitats = world.Endless!.Habitats;
        for (int i = 0; i < habitats.Count; i++)
        {
            if (near.NearestWithin(habitats[i].Cell) <= reach)
            {
                active.Add(i);
            }
        }
        return active;
    }

    /// <summary>The custom maps' habitats, as kinds to copy.</summary>
    private List<(int Mission, int Point)> Sources()
    {
        var sources = new List<(int Mission, int Point)>();
        foreach (MissionRules mission in world.Content.Rules.Missions.Missions)
        {
            if (mission.Id < MatchStart.FirstMapMission)
            {
                continue;
            }
            for (int point = 0; point < mission.AnimalSpawnPoints.Count; point++)
            {
                sources.Add((mission.Id, point));
            }
        }
        return sources;
    }
}
