using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Missions;

/// <summary>
/// Puts a mission's starting objects into the world: its buildings, units and animals, and the
/// tribes' starting stock; and the switch to the second part of a two-part mission, where the
/// whole map comes into play and the next mission's objects join the world as it is.
/// </summary>
internal sealed class MissionSetup
{
    /// <summary>The kind of the animals a mission starts with.</summary>
    public const int StartAnimalKind = 1;

    /// <summary>Stock counters (0..10) a mission sets, one per initial object.</summary>
    private const int StockCounters = 11;

    /// <summary>The mission whose units start all male; it keeps the gender balance afterwards.</summary>
    private const int AllMaleMission = 3;

    /// <summary>The first mission, the tutorial: two of its units start as builders.</summary>
    private const int TutorialMission = 0;

    /// <summary>Gender modes of new units: all male, keep the balance.</summary>
    private const int AllMale = 0, BalancedGenders = 2;

    /// <summary>The tutorial's builders and their building skill.</summary>
    private static readonly (int Unit, int Skill)[] TutorialBuilders = { (2, 4), (5, 5) };

    private readonly GameWorld world;
    private readonly BuildingPlacement placement;
    private readonly UnitSpawning spawning;
    private readonly WildlifeAndNodes objects;

    /// <summary>Sets up missions in <paramref name="world"/>.</summary>
    public MissionSetup(GameWorld world, BuildingPlacement placement, UnitSpawning spawning, WildlifeAndNodes objects)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.placement = placement ?? throw new ArgumentNullException(nameof(placement));
        this.spawning = spawning ?? throw new ArgumentNullException(nameof(spawning));
        this.objects = objects ?? throw new ArgumentNullException(nameof(objects));
    }

    /// <summary>
    /// Places the objects of the mission set in <see cref="GameWorld.Mission"/> in order and sets
    /// the starting stock (missions that continue another keep the stock they have).
    /// </summary>
    public void PlaceObjects()
    {
        MissionRules mission = world.Content.Rules.Missions.Missions[world.Mission];
        if (world.Mission == AllMaleMission)
        {
            world.SpawnGenderMode = AllMale;
        }
        IReadOnlyList<InitialObject> list = mission.InitialObjects;
        bool setsStock = !KeepsStock(world.Mission);
        for (int i = 0; i < list.Count; i++)
        {
            Place(list[i]);
            if (i < StockCounters && setsStock)
            {
                SetStock(mission, i);
            }
        }
        if (world.Mission == TutorialMission)
        {
            foreach ((int unit, int skill) in TutorialBuilders)
            {
                world.Unit(unit).BuildingSkill = skill;
            }
        }
        else if (world.Mission == AllMaleMission)
        {
            world.SpawnGenderMode = BalancedGenders;
        }
    }

    /// <summary>
    /// Brings the whole map into play and continues with the next mission: its objects are added to
    /// the world as it is (the counters are kept).
    /// </summary>
    public void SwitchToNextPart()
    {
        world.Map.PlayableSize = world.Map.Size;
        world.Mission++;
        PlaceObjects();
    }

    /// <summary>Missions that continue the one before in the same world keep their stock.</summary>
    public static bool KeepsStock(int mission) => mission is 1 or 6;

    /// <summary>Places one initial object as the mission does (a computer tribe's units add their difficulty to their skill).</summary>
    public void Place(InitialObject item)
    {
        ArgumentNullException.ThrowIfNull(item);
        switch (item.Kind)
        {
            case InitialObjectKind.Building:
                placement.Place(new PlacementRequest(Required(item.Tribe), Required(item.BuildingKind), item.Cell,
                    Required(item.Level), Immediate: true));
                break;
            case InitialObjectKind.Unit:
                int tribe = Required(item.Tribe);
                int traits = Required(item.Skill) + (TribeRoles.IsComputer(world, tribe) ? TribeRoles.Difficulty(world, tribe) : 0);
                spawning.Spawn(tribe, item.Cell, Required(item.Age), new UnitTraits(Wrap.Signed8(traits)));
                break;
            default:
                objects.SpawnAnimal(StartAnimalKind, item.Cell, Required(item.SpawnPoint));
                break;
        }
    }

    /// <summary>
    /// Gives <paramref name="tribe"/> the start stock (counters 0..10) the mission gives tribe
    /// <paramref name="source"/> (0 the player's, 1 the computer's).
    /// </summary>
    public void SetStartStock(int tribe, int source)
    {
        MissionRules mission = world.Content.Rules.Missions.Missions[world.Mission];
        if (source < 0 || source >= mission.StartStock.Count)
        {
            return;
        }
        foreach (CounterAmount stock in mission.StartStock[source])
        {
            world.Tribes[tribe][stock.Counter] = stock.Amount;
        }
    }

    private void SetStock(MissionRules mission, int counter)
    {
        for (int tribe = 0; tribe < world.Tribes.Count && tribe < mission.StartStock.Count; tribe++)
        {
            CounterAmount stock = mission.StartStock[tribe][counter];
            world.Tribes[tribe][stock.Counter] = stock.Amount;
        }
    }

    private static int Required(int? value) =>
        value ?? throw new InvalidOperationException("initial object without a value its kind needs");
}
