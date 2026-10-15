using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>
/// Starting a match (versus or custom game) on one of the custom maps: the mission's map, its
/// animals and its rules, and instead of the mission's two sides a camp for every tribe of the
/// match. Human-ruled tribes get the stock and the rules of the original's player, computer tribes
/// the computer's (its plan laid onto their camp). Tribes can join later (<see cref="AddTribe"/>).
/// </summary>
public static class MatchStart
{
    /// <summary>The mission of custom map 0; custom map m is mission FirstMapMission + m.</summary>
    public const int FirstMapMission = MissionStart.FirstCustomMission;

    /// <summary>The spawn gender mode a game starts with (keep the balance of men and women).</summary>
    private const int BalancedGenders = 2;

    /// <summary>Each content's custom maps' capacities, worked out when first asked (-1 not yet).</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameContent, int[]> Capacities = new();

    /// <summary>Mixed into the seed for the draw that hands the camps to the tribes (it does not use the rules' generator).</summary>
    private const long CampSeedMix = 0x43414D50; // "CAMP"

    /// <summary>
    /// The highest skill level a unit starts with in the original (with the computer's difficulty
    /// added); a computer tribe's camp units, which add the difficulty to the player's camp skills,
    /// are kept to it (a faster unit would have no gait).
    /// </summary>
    private const int HighestStartSkill = 4;

    /// <summary>The mission's player tribe, whose start camp every tribe gets.</summary>
    private const int OriginalPlayer = 0;

    /// <summary>The start stock of human-ruled and of computer tribes: the mission's player's and computer's.</summary>
    private const int PlayerStock = 0, ComputerStock = 1;

    /// <summary>The custom maps there are in <paramref name="content"/>.</summary>
    public static int MapCount(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Math.Max(0, content.Missions.Count - FirstMapMission);
    }

    /// <summary>
    /// How many camps custom map <paramref name="map"/> has room for (see <see cref="CampPlacer"/>):
    /// the most tribes a match on it can start with, every camp with room to build on its own side
    /// (<see cref="CampRoom"/>) for that many tribes and for any fewer. It depends on the map only
    /// (worked out once per content).
    /// </summary>
    public static int CampCapacity(GameContent content, int map)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (map < 0 || map >= MapCount(content))
        {
            throw new ArgumentOutOfRangeException(nameof(map), map, $"custom maps are 0..{MapCount(content) - 1}");
        }
        int[] known = Capacities.GetValue(content, c => Enumerable.Repeat(-1, MapCount(c)).ToArray());
        lock (known)
        {
            if (known[map] < 0)
            {
                known[map] = WorkOutCapacity(content, map);
            }
            return known[map];
        }
    }

    private static int WorkOutCapacity(GameContent content, int map)
    {
        RuleSystems systems = NewWorld(content, new MatchSetup { Map = map, Tribes = [new MatchTribe(TribeController.Human(0))] }, animals: false);
        var placer = new CampPlacer(systems.World, Template(systems.World));
        int most = placer.Layout(int.MaxValue).Count;
        int capacity = Math.Min(1, most);
        for (int count = 2; count <= most && placer.HaveRoom(placer.Arrange(count)); count++)
        {
            capacity = count;
        }
        return capacity;
    }

    /// <summary>
    /// How many camps custom map <paramref name="map"/> has room for at <see cref="CampPlacer.RoomyDistance"/>
    /// from each other; a match with more tribes (up to <see cref="CampCapacity"/>) places them as far
    /// apart as the map allows.
    /// </summary>
    public static int RoomyCampCapacity(GameContent content, int map)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (map < 0 || map >= MapCount(content))
        {
            throw new ArgumentOutOfRangeException(nameof(map), map, $"custom maps are 0..{MapCount(content) - 1}");
        }
        RuleSystems systems = NewWorld(content, new MatchSetup { Map = map, Tribes = [new MatchTribe(TribeController.Human(0))] }, animals: false);
        return new CampPlacer(systems.World, Template(systems.World)).Layout(int.MaxValue, CampPlacer.RoomyDistance).Count;
    }

    /// <summary>
    /// Why <paramref name="setup"/> cannot start a match (no tribe, a map there is not, more tribes
    /// than the map has room for), or null when it can.
    /// </summary>
    public static string? Problem(GameContent content, MatchSetup setup)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.Tribes.Count == 0)
        {
            return "a match needs a tribe";
        }
        if (setup.Map < 0 || setup.Map >= MapCount(content))
        {
            return $"custom maps are 0..{MapCount(content) - 1}";
        }
        if (setup.Settings.Problem() is string settings)
        {
            return settings;
        }
        int capacity = CampCapacity(content, setup.Map);
        return setup.Tribes.Count > capacity
            ? $"custom map {setup.Map} has room for {capacity} camps, not {setup.Tribes.Count}"
            : null;
    }

    /// <summary>A new world playing <paramref name="setup"/>, with its rule systems.</summary>
    /// <exception cref="ArgumentException">The setup cannot start a match (see <see cref="Problem"/>).</exception>
    public static RuleSystems Begin(GameContent content, MatchSetup setup)
    {
        if (Problem(content, setup) is string problem)
        {
            throw new ArgumentException(problem, nameof(setup));
        }
        RuleSystems systems = NewWorld(content, setup, animals: false);
        GameWorld world = systems.World;
        CampIndex.Starting(world);
        List<GridPoint> camps = new CampPlacer(world, Template(world)).Arrange(setup.Tribes.Count);
        Shuffle(camps, new GameRandom(setup.Seed ^ CampSeedMix));
        for (int t = 0; t < setup.Tribes.Count; t++)
        {
            Settle(systems, t, camps[t]);
        }
        PlaceAnimals(systems);
        systems.Sight.UpdateSight();
        return systems;
    }

    /// <summary>
    /// Adds a tribe to the match of <paramref name="systems"/> while it runs: played by
    /// <paramref name="controller"/> on <paramref name="team"/>, with a camp where
    /// <paramref name="finder"/> finds room (by default <see cref="MapCampFinder"/>: on the map as it
    /// is, away from the other tribes). Returns its number, or -1 (and nothing changes) when there is
    /// no room for a camp.
    /// </summary>
    public static int AddTribe(RuleSystems systems, TribeController controller, int team, ICampFinder? finder = null)
    {
        ArgumentNullException.ThrowIfNull(systems);
        GameWorld world = systems.World;
        if ((finder ?? MapCampFinder.Instance).FindCamp(world) is not GridPoint origin)
        {
            return -1;
        }
        int tribe = world.AddTribe(controller, team);
        systems.MissionReset.ResetComputer(world.Tribes[tribe].Ai);
        if (controller.IsHumanRuled)
        {
            world.Tribes[tribe][TribeCounter.LivingSpace] = MissionReset.StartLivingSpace;
        }
        Settle(systems, tribe, origin);
        systems.Sight.UpdateSight();
        return tribe;
    }

    /// <summary>The camp every tribe of the match of <paramref name="world"/> starts with.</summary>
    internal static CampTemplate Template(GameWorld world) =>
        CampTemplate.Of(world.Content.Rules.Missions.Missions[world.Mission], OriginalPlayer);

    /// <summary>The world of <paramref name="setup"/> before its camps: the map, controllers and teams, and with <paramref name="animals"/> the map's animals.</summary>
    private static RuleSystems NewWorld(GameContent content, MatchSetup setup, bool animals)
    {
        int mission = FirstMapMission + setup.Map;
        MissionDefinition definition = content.Missions[mission];
        var world = new GameWorld(content, new WorldMap(content.Maps[definition.MapId].Size, definition.PlayableSize),
            WorldLayout.ForMatch(setup.Tribes.Count), new GameRandom(setup.Seed))
        {
            Difficulty = setup.Difficulty,
            SpawnGenderMode = BalancedGenders,
        };
        world.Mode = WorldMode.Match;
        world.Match.EndsWithLastSide = setup.EndsWithLastSide;
        world.Settings = setup.Settings;
        for (int t = 0; t < setup.Tribes.Count; t++)
        {
            world.SetController(t, setup.Tribes[t].Controller);
            world.SetTeam(t, setup.Tribes[t].Team);
            world.Tribes[t].Hints.Enabled = setup.HintsEnabled;
        }
        var systems = new RuleSystems(world);
        world.Mission = mission;
        systems.MissionReset.Reset();
        systems.MissionStarter.BuildMap();
        if (animals)
        {
            foreach (InitialObject item in world.Content.Rules.Missions.Missions[world.Mission].InitialObjects)
            {
                if (item.Kind == InitialObjectKind.Animal)
                {
                    systems.MissionSetup.Place(item);
                }
            }
        }
        return systems;
    }

    /// <summary>The map's animals, when the world has wild animals, but none on the land around a camp (<see cref="CampLand"/>).</summary>
    private static void PlaceAnimals(RuleSystems systems)
    {
        GameWorld world = systems.World;
        if (!world.Settings.WildAnimals)
        {
            return;
        }
        foreach (InitialObject item in world.Content.Rules.Missions.Missions[world.Mission].InitialObjects)
        {
            if (item.Kind == InitialObjectKind.Animal && !CampLand.IsNearACamp(world, item.Cell))
            {
                systems.MissionSetup.Place(item);
            }
        }
    }

    /// <summary>The camps handed to the tribes in an order drawn from the match's seed.</summary>
    private static void Shuffle(List<GridPoint> camps, GameRandom random)
    {
        for (int i = camps.Count - 1; i > 0; i--)
        {
            int j = random.NextMasked(int.MaxValue, i + 1);
            (camps[i], camps[j]) = (camps[j], camps[i]);
        }
    }

    /// <summary>
    /// Puts the camp of <paramref name="tribe"/> at <paramref name="origin"/>: its buildings and
    /// units, its start stock, its chief (human-ruled tribes; the computer has none, as in the
    /// original), and the camp as the origin of its computer's plan.
    /// </summary>
    internal static void Settle(RuleSystems systems, int tribe, GridPoint origin)
    {
        GameWorld world = systems.World;
        world.SetCamp(tribe, origin);
        CampIndex.Settled(world, tribe);
        int firstUnit = 0;
        foreach (InitialObject item in Template(world).Objects)
        {
            var cell = new GridPoint(origin.X + item.Cell.X, origin.Y + item.Cell.Y);
            int before = world.Tribes[tribe][TribeCounter.Population];
            systems.MissionSetup.Place(item with { Tribe = tribe, Cell = cell, Skill = StartSkill(world, tribe, item.Skill) });
            if (item.Kind == InitialObjectKind.Unit && firstUnit == 0 && world.Tribes[tribe][TribeCounter.Population] > before
                && world.Map.CellAt(cell) is { Content: CellContent.Unit } placed)
            {
                firstUnit = placed.Index;
            }
        }
        bool human = TribeRoles.IsPlayer(world, tribe);
        int stock = human ? PlayerStock : ComputerStock;
        systems.MissionSetup.SetStartStock(tribe, stock);
        ScaleStock(world, tribe, stock);
        world.Tribes[tribe][TribeCounter.Chief] = human ? firstUnit : 0;
    }

    /// <summary>The start stock of <paramref name="tribe"/> (the mission's list <paramref name="source"/>) scaled to the world's setting.</summary>
    private static void ScaleStock(GameWorld world, int tribe, int source)
    {
        int percent = world.Settings.StartStockPercent;
        IReadOnlyList<IReadOnlyList<CounterAmount>> lists = world.Content.Rules.Missions.Missions[world.Mission].StartStock;
        if (percent == WorldSettings.OriginalStock || source >= lists.Count)
        {
            return;
        }
        foreach (CounterAmount stock in lists[source])
        {
            world.Tribes[tribe][stock.Counter] = (int)((long)stock.Amount * percent / WorldSettings.OriginalStock);
        }
    }

    /// <summary>The skill a camp unit is placed with (the computer adds its difficulty when it is placed).</summary>
    private static int? StartSkill(GameWorld world, int tribe, int? skill) =>
        skill is int level && TribeRoles.IsComputer(world, tribe)
            ? Math.Max(0, Math.Min(level, HighestStartSkill - TribeRoles.Difficulty(world, tribe)))
            : skill;
}
