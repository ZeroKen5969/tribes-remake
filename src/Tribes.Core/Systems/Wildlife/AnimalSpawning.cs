using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Search;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Wildlife;

/// <summary>
/// New animals: every so often one is born in a habitat that has room, on a free cell near the
/// habitat's centre, of a kind drawn by the habitat's odds. In the migration mission the herd of
/// the first habitat may set out for another one.
/// </summary>
internal sealed class AnimalSpawning
{
    /// <summary>The spawn interval is counted in steps of this many frames.</summary>
    public const int IntervalFrames = 40;

    /// <summary>An animal is born when the frame number is this past a multiple of the interval.</summary>
    public const int BirthPhase = 10;

    /// <summary>How far around the habitat's centre a free cell is looked for (spiral rounds).</summary>
    private const int BirthPlaceRounds = 10;

    /// <summary>The rolls: habitat, kind (percent) and the herd setting out (tenths).</summary>
    private const int RollMask = 0xFFFFFF, Percent = 100, MigrationOdds = 10;

    /// <summary>The herd may set out once its habitat holds more than this many animals.</summary>
    private const int MigrationHerd = 1;

    private readonly GameWorld world;
    private readonly SpiralSearch spiral;
    private readonly WildlifeAndNodes objects;
    private readonly Endless.EndlessWildlife? endless;

    /// <summary>Brings animals into <paramref name="world"/> (an endless world's by <paramref name="endless"/>).</summary>
    public AnimalSpawning(GameWorld world, SpiralSearch spiral, WildlifeAndNodes objects, Endless.EndlessWildlife? endless = null)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
        this.objects = objects ?? throw new ArgumentNullException(nameof(objects));
        this.endless = endless;
    }

    /// <summary>
    /// Brings an animal into the world if one is due this frame: a random habitat, or the first
    /// one with room if that is full; none when every habitat is full. The migration mission has
    /// no births before its first frames have passed.
    /// </summary>
    public void SpawnIfDue()
    {
        if (world.Endless != null && endless != null)
        {
            endless.SpawnIfDue();
            return;
        }
        MissionRules mission = world.Content.Rules.Missions.Missions[world.Mission];
        int habitats = mission.ActiveAnimalSpawnPoints;
        bool due = habitats > 0 && world.Frame % (mission.AnimalSpawnInterval * IntervalFrames) == BirthPhase
            && (world.Mission != AnimalMissions.MigrationMission || world.Frame > AnimalMissions.MigrationBirthDelay);
        if (!due || (world.Mode == WorldMode.Match && !world.Settings.WildAnimals))
        {
            return;
        }
        int zone = world.Random.NextMasked(RollMask, habitats);
        if (world.AnimalZoneCount(zone) >= mission.AnimalsPerSpawnPoint)
        {
            zone = 0;
            while (zone < habitats && world.AnimalZoneCount(zone) >= mission.AnimalsPerSpawnPoint)
            {
                zone++;
            }
        }
        if (zone >= habitats)
        {
            return;
        }
        AnimalSpawnPoint habitat = mission.AnimalSpawnPoints[zone];
        if (KindFor(world, habitat.KindCumulativePercent) is { } kind
            && Match.CampLand.BirthPlace(world, spiral, habitat.Cell, BirthPlaceRounds) is { } cell)
        {
            objects.SpawnAnimal(kind, cell, zone);
        }
        if (world.Mission == AnimalMissions.MigrationMission)
        {
            MaybeMigrate();
        }
    }

    /// <summary>
    /// The kind of an animal born where <paramref name="cumulativePercent"/> gives the odds. In a match
    /// or an endless world the fierce kind keeps only <see cref="EndlessWildlifeRules.FierceSharePercent"/>
    /// of its odds and the rest go to the other kinds by theirs; null when the place breeds nothing
    /// else then. The campaign draws as the original (one draw).
    /// </summary>
    internal static int? KindFor(GameWorld world, IReadOnlyList<int> cumulativePercent)
    {
        int kind = KindFor(cumulativePercent, world.Random.NextMasked(RollMask, Percent));
        if (world.Mode != WorldMode.Match || world.Content.Endless?.Wildlife is not { } rules
            || kind != rules.FierceKind || world.Random.NextMasked(RollMask, Percent) < rules.FierceSharePercent)
        {
            return kind;
        }
        int from = kind == 0 ? 0 : cumulativePercent[kind - 1], to = cumulativePercent[kind];
        int others = Percent - (to - from);
        if (others <= 0)
        {
            return null;
        }
        int roll = world.Random.NextMasked(RollMask, others);
        return KindFor(cumulativePercent, roll < from ? roll : roll + (to - from));
    }

    /// <summary>The first kind whose cumulative percent exceeds <paramref name="roll"/>.</summary>
    internal static int KindFor(IReadOnlyList<int> cumulativePercent, int roll)
    {
        int kind = 0;
        while (kind < cumulativePercent.Count - 1 && roll >= cumulativePercent[kind])
        {
            kind++;
        }
        return kind;
    }

    /// <summary>
    /// The herd of the first habitat sets out (its count is cleared), more likely the bigger it is.
    /// Every animal slot of that habitat is moved, free slots included.
    /// </summary>
    private void MaybeMigrate()
    {
        int herd = world.AnimalZoneCount(AnimalMissions.HomeZone);
        if (herd <= MigrationHerd || world.Random.NextMasked(RollMask, MigrationOdds) >= herd)
        {
            return;
        }
        for (int i = 0; i < world.AnimalSlots; i++)
        {
            Animal animal = world.Animal(i);
            if (animal.SpawnZone == AnimalMissions.HomeZone)
            {
                animal.SpawnZone = AnimalMissions.MigratingZone;
            }
        }
        world.SetAnimalZoneCount(AnimalMissions.HomeZone, 0);
    }
}
