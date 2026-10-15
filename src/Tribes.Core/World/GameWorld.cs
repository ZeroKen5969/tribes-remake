using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Tribes.Core.Content;
using Tribes.Core.Maps;

namespace Tribes.Core.World;

/// <summary>How many slots of each kind a world starts with (see <see cref="GameWorld.UnitsOf"/> for the slots it has).</summary>
/// <param name="Tribes">Number of tribes (it grows when a tribe joins a game in progress).</param>
/// <param name="UnitsPerTribe">
/// Unit slots each tribe gets when it is added (the original's 50: tribe t's are then t*50+1 ..
/// (t+1)*50), also the most a tribe has where slots are limited; 0 when tribes get their slots as
/// they need them (<see cref="ForMatch"/>).
/// </param>
/// <param name="BuildingsPerTribe">Building slots each tribe gets when it is added, in the same way (the original's 30).</param>
/// <param name="Animals">Wild animal slots.</param>
/// <param name="FogSize">Size of each tribe's fog grid.</param>
public sealed record WorldLayout(int Tribes, int UnitsPerTribe, int BuildingsPerTribe, int Animals, GridSize FogSize)
{
    /// <summary>The original game's layout: two tribes of 50 units and 30 buildings, 22 animals.</summary>
    public static WorldLayout Original { get; } = new(2, 50, 30, 22, new GridSize(96, 96));

    /// <summary>The original layout with <paramref name="tribes"/> tribes.</summary>
    public static WorldLayout ForTribes(int tribes) =>
        tribes >= 1 ? Original with { Tribes = tribes } : throw new ArgumentOutOfRangeException(nameof(tribes));

    /// <summary>
    /// The layout of a match of <paramref name="tribes"/> tribes: no slot to start with, every tribe
    /// gets its slots as it needs them (no limit; see <see cref="GameWorld.GrowsOnDemand"/>).
    /// </summary>
    public static WorldLayout ForMatch(int tribes) => ForTribes(tribes) with { UnitsPerTribe = 0, BuildingsPerTribe = 0 };
}

/// <summary>Which rules a world plays by where the original's tribes share state.</summary>
public enum WorldMode
{
    /// <summary>
    /// The original's single-player game: the player's army calls and hint channels and the
    /// computer's state are the whole world's, as the original keeps them (every tribe's units
    /// read them).
    /// </summary>
    Campaign,

    /// <summary>A match of several tribes (versus, endless): every tribe's rules read only its own state.</summary>
    Match,
}

/// <summary>
/// Everything the rules know about a game in progress. Systems in <c>Tribes.Core.Systems</c> read
/// and change it; nothing here depends on how the game is shown. Tribes can be added while the
/// game runs (<see cref="AddTribe"/>) and slots and tables only ever get added at the end, so
/// nothing is renumbered.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>Dust cloud slots of the original (a match's table grows when all are taken, see <see cref="GrowsOnDemand"/>).</summary>
    public const int DustSlots = 30;

    /// <summary>Trap order slots of the original (a match's table grows when all are taken).</summary>
    public const int TrapSlots = 30;

    /// <summary>Construction site slots of the original (a match's table grows when all are taken).</summary>
    public const int ConstructionSlots = 10;

    /// <summary>Upgrade and repair slots of the original (a match's table grows when all are taken).</summary>
    public const int UpgradeSlots = 20;

    /// <summary>Couple slots of the original (a match's table grows when all are taken).</summary>
    public const int CoupleSlots = 10;

    /// <summary>The most couple slots the original's couple link of a unit can name (a match's links name any number).</summary>
    public const int MostCoupleSlots = 63;

    /// <summary>Animal spawn zones.</summary>
    public const int AnimalZones = 3;

    /// <summary>Building kinds that can be unlocked.</summary>
    public const int UnlockableKinds = 12;

    /// <summary>The tribes of the original, which the shared tables above are sized for (and the object groups at least).</summary>
    private const int SharedTableTribes = 2;

    private readonly List<Unit> units = new();
    private readonly List<Building> buildings = new();
    private readonly List<Animal> animals = new();
    private readonly List<TribeState> tribes = new();
    private readonly List<FogOfWar> fog = new();
    private readonly List<DustCloud> dust = new();
    private readonly List<TrapOrder> traps = new();
    private readonly List<ConstructionJob> construction = new();
    private readonly List<UpgradeJob> upgrades = new();
    private readonly List<Couple> couples = new();
    private readonly int[] animalZoneCounts = new int[AnimalZones];
    private readonly int[] unlockLevels = new int[UnlockableKinds];
    private int mission, difficulty, endCountdown, framesSinceCombat, spawnGenderMode;
    private WorldMode mode;

    /// <summary>
    /// Creates a world with empty slots on <paramref name="map"/>. Tribe 0 is played by a human in
    /// slot 0 and every other tribe by the computer, all enemies, as in the original.
    /// </summary>
    public GameWorld(GameContent content, WorldMap map, WorldLayout layout, GameRandom random)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Map = map ?? throw new ArgumentNullException(nameof(map));
        ArgumentNullException.ThrowIfNull(layout);
        Random = random ?? throw new ArgumentNullException(nameof(random));
        if (layout.Tribes < 1 || layout.UnitsPerTribe < 0 || layout.BuildingsPerTribe < 0 || layout.Animals < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "a world needs a tribe");
        }
        Layout = layout with { Tribes = 0 };
        units.Add(null!);
        buildings.Add(null!);
        for (int i = 0; i < layout.Animals; i++)
        {
            AddAnimalSlot();
        }
        Tribes = new ListView<TribeState>(tribes);
        Relations = new TribeRelations(Tribes);
        Dust = new ListView<DustCloud>(dust);
        Traps = new ListView<TrapOrder>(traps);
        Construction = new ListView<ConstructionJob>(construction);
        Upgrades = new ListView<UpgradeJob>(upgrades);
        Couples = new ListView<Couple>(couples);
        GrowTables(SharedTableTribes);
        Sectors = map.IsChunked ? SectorIndex.ForChunks(map) : new SectorIndex(0);
        for (int t = 0; t < layout.Tribes; t++)
        {
            AddTribe(t == 0 ? TribeController.Human(0) : TribeController.Computer());
        }
    }

    /// <summary>The game's content (tables and maps).</summary>
    public GameContent Content { get; }

    /// <summary>Terrain, cell contents and resource nodes (a story scene may rebuild it for another mission).</summary>
    public WorldMap Map { get; internal set; }

    /// <summary>The slots tribes are given when they are added (<see cref="WorldLayout.Tribes"/> follows the tribes added).</summary>
    public WorldLayout Layout { get; private set; }

    /// <summary>The only random generator the rules use.</summary>
    public GameRandom Random { get; }

    /// <summary>
    /// Whether the original's shared state applies (<see cref="WorldMode"/>); set when a match is
    /// made (a match's units then take the couple links that name any couple slot).
    /// </summary>
    public WorldMode Mode
    {
        get => mode;
        internal set
        {
            bool becomesMatch = value == WorldMode.Match && mode != WorldMode.Match;
            mode = value;
            if (becomesMatch)
            {
                WidenLinks();
            }
        }
    }

    /// <summary>The state of the match as a whole (in a match).</summary>
    public MatchState Match { get; } = new();

    /// <summary>The endless world's own state (its seed, habitats, invitations); null for other worlds.</summary>
    public EndlessState? Endless { get; internal set; }

    /// <summary>The settings the world was started with (the campaign's are <see cref="WorldSettings.Original"/>).</summary>
    public WorldSettings Settings { get; internal set; } = WorldSettings.Original;

    /// <summary>Logic frames since the mission started.</summary>
    public int Frame { get; set; }

    /// <summary>Counter of the periodic sweep that clears spent trap cells, a row at a time.</summary>
    public int RowSweep { get; set; }

    /// <summary>The mission being played.</summary>
    public int Mission { get => mission; set => mission = Wrap.Signed8(value); }

    /// <summary>Difficulty level (the computer tribes' unless their controller names one).</summary>
    public int Difficulty { get => difficulty; set => difficulty = Wrap.Signed8(value); }

    /// <summary>Victory/defeat countdown: 0 none, counts up from 1 (victory) or 50 (defeat).</summary>
    public int EndCountdown { get => endCountdown; set => endCountdown = Wrap.Signed8(value); }

    /// <summary>Frames since a unit last fought (up to 50); quiet time gates hints and unlocks.</summary>
    public int FramesSinceCombat { get => framesSinceCombat; set => framesSinceCombat = Wrap.Signed8(value); }

    /// <summary>Gender of spawned units: 0 male, 1 female, 2 keep the balance.</summary>
    public int SpawnGenderMode { get => spawnGenderMode; set => spawnGenderMode = Wrap.Signed8(value); }

    /// <summary>Per-tribe state, by tribe number.</summary>
    public ListView<TribeState> Tribes { get; }

    /// <summary>Alliances and teams.</summary>
    public TribeRelations Relations { get; }

    /// <summary>Unit slot <paramref name="id"/> (1-based).</summary>
    public Unit Unit(int id) => units[CheckSlot(id, units.Count)];

    /// <summary>Number of unit slots.</summary>
    public int UnitSlots => units.Count - 1;

    /// <summary>Building slot <paramref name="id"/> (1-based).</summary>
    public Building Building(int id) => buildings[CheckSlot(id, buildings.Count)];

    /// <summary>Number of building slots.</summary>
    public int BuildingSlots => buildings.Count - 1;

    /// <summary>Animal slot <paramref name="index"/> (0-based).</summary>
    public Animal Animal(int index) => animals[index];

    /// <summary>Number of animal slots (an endless world adds slots as animals are born).</summary>
    public int AnimalSlots => animals.Count;

    /// <summary>Adds a free animal slot at the end and returns it.</summary>
    internal Animal AddAnimalSlot()
    {
        var animal = new Animal(animals.Count, wideZones: Map.IsChunked);
        animals.Add(animal);
        return animal;
    }

    /// <summary>What <paramref name="tribe"/> has explored and sees.</summary>
    public FogOfWar Fog(int tribe) => fog[tribe];

    /// <summary>Dust clouds.</summary>
    public ListView<DustCloud> Dust { get; }

    /// <summary>Ordered and set traps.</summary>
    public ListView<TrapOrder> Traps { get; }

    /// <summary>Construction sites waiting for material.</summary>
    public ListView<ConstructionJob> Construction { get; }

    /// <summary>Upgrades and repairs in progress.</summary>
    public ListView<UpgradeJob> Upgrades { get; }

    /// <summary>Couples on their way to a hut.</summary>
    public ListView<Couple> Couples { get; }

    /// <summary>Spatial index used to find nearby targets.</summary>
    public SectorIndex Sectors { get; }

    /// <summary>Scenes, objectives, tutorial.</summary>
    public ScriptState Script { get; } = new();

    /// <summary>
    /// The state of the original's computer player: that of <see cref="TribeRoles.Computer"/>
    /// (each tribe has its own, <see cref="TribeState.Ai"/>).
    /// </summary>
    public AiState Ai => tribes[TribeRoles.Computer(this)].Ai;

    /// <summary>
    /// The original player's army-wide orders: those of <see cref="TribeRoles.Player"/> (each tribe
    /// has its own, <see cref="TribeState.Army"/>).
    /// </summary>
    public ArmyOrders Orders => tribes[TribeRoles.Player(this)].Army;

    /// <summary>
    /// The original player's hint option and cool-downs: those of <see cref="TribeRoles.Player"/>
    /// (each tribe has its own, <see cref="TribeState.Hints"/>).
    /// </summary>
    public HintState Hints => tribes[TribeRoles.Player(this)].Hints;

    /// <summary>Animals alive in spawn zone <paramref name="zone"/> (in an endless world: habitat <paramref name="zone"/>).</summary>
    public int AnimalZoneCount(int zone) => Endless != null ? Endless.Habitats[zone].Animals : animalZoneCounts[zone];

    /// <summary>Sets the animals alive in spawn zone <paramref name="zone"/> (in an endless world: habitat <paramref name="zone"/>).</summary>
    public void SetAnimalZoneCount(int zone, int count)
    {
        if (Endless != null)
        {
            Endless.Habitats[zone].Animals = count;
            return;
        }
        animalZoneCounts[zone] = Wrap.Signed8(count);
    }

    /// <summary>Highest level of building kind <paramref name="kind"/> that may be built now.</summary>
    public int UnlockLevel(int kind) => unlockLevels[kind];

    /// <summary>Sets the highest buildable level of <paramref name="kind"/>.</summary>
    public void SetUnlockLevel(int kind, int level) => unlockLevels[kind] = Wrap.Signed8(level);

    /// <summary>
    /// Adds an empty tribe played by <paramref name="controller"/> on <paramref name="team"/>
    /// (<see cref="TribeState.NoTeam"/>: allied with no one): the slots the layout gives every tribe
    /// (none in a match, whose tribes get slots as they need them), its fog and its sector categories
    /// come after everything there is, so no slot changes its number. Returns the new tribe's number.
    /// Its camp is placed by the match rules.
    /// </summary>
    public int AddTribe(TribeController controller, int team = TribeState.NoTeam)
    {
        int tribe = tribes.Count;
        WorldLayout layout = Layout with { Tribes = tribe + 1 };
        AddSlotsOf(tribe, layout);
        tribes.Add(new TribeState(tribe) { Controller = controller, Team = team < 0 ? TribeState.NoTeam : team, ArrivedAtFrame = Frame });
        fog.Add(Map.IsChunked ? FogOfWar.ForChunks(Map) : new FogOfWar(layout.FogSize));
        Layout = layout;
        Sectors.AddCategories(ObjectGroupsFor(layout.Tribes) - Sectors.CategoryCount);
        return tribe;
    }

    /// <summary>
    /// Object groups (sector index categories) of a world of <paramref name="tribeCount"/> tribes:
    /// the units and the buildings of each tribe and the animals (at least the original's five).
    /// </summary>
    public static int ObjectGroupsFor(int tribeCount) => 1 + (2 * Math.Max(SharedTableTribes, tribeCount));

    /// <summary>Sets who plays <paramref name="tribe"/> (the rules of <see cref="TribeRoles"/> follow it).</summary>
    internal void SetController(int tribe, TribeController controller) => tribes[tribe].Controller = controller;

    /// <summary>Records where the camp of <paramref name="tribe"/> was placed.</summary>
    internal void SetCamp(int tribe, GridPoint camp) => tribes[tribe].Camp = camp;

    /// <summary>Puts <paramref name="tribe"/> on <paramref name="team"/> (<see cref="TribeState.NoTeam"/> for none).</summary>
    internal void SetTeam(int tribe, int team)
    {
        tribes[tribe].Team = team < 0 ? TribeState.NoTeam : team;
        Relations.Changed();
    }

    /// <summary>
    /// Grows the shared tables to the size worlds of <paramref name="tribeCount"/> tribes had before
    /// the tables grew on demand: the original's for two tribes, and half as much again per further
    /// tribe (the couples at most <see cref="MostCoupleSlots"/> on a fixed map). Saves of those
    /// formats hold tables of that size.
    /// </summary>
    private void GrowTables(int tribeCount)
    {
        int share = Math.Max(SharedTableTribes, tribeCount);
        GrowTo(dust, DustSlots * share / SharedTableTribes, () => new DustCloud());
        GrowTo(traps, TrapSlots * share / SharedTableTribes, () => new TrapOrder());
        GrowTo(construction, ConstructionSlots * share / SharedTableTribes, () => new ConstructionJob());
        GrowTo(upgrades, UpgradeSlots * share / SharedTableTribes, () => new UpgradeJob());
        int coupleSlots = CoupleSlots * share / SharedTableTribes;
        GrowTo(couples, Map.IsChunked ? coupleSlots : Math.Min(MostCoupleSlots, coupleSlots), () => new Couple());
    }

    private static void GrowTo<T>(List<T> table, int count, Func<T> create)
    {
        while (table.Count < count)
        {
            table.Add(create());
        }
    }

    private static int CheckSlot(int id, int length) =>
        id >= 1 && id < length ? id : throw new ArgumentOutOfRangeException(nameof(id), id, $"slots are 1..{length - 1}");
}
