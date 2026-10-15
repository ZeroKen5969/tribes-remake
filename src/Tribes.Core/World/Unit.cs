using System;

namespace Tribes.Core.World;

/// <summary>
/// One tribe member. Units live in fixed slots (<see cref="Id"/>); a slot is free when
/// <see cref="HitPoints"/> is 0. Quantities keep the original game's value range: storing a value
/// outside it wraps around as the original's byte storage does (see <see cref="Wrap"/>), which some
/// rules rely on. Positions and references to other entities are plain integers so that large maps
/// and many tribes fit.
/// </summary>
public sealed class Unit
{
    private int workProgress, happiness, facing, pathIndex, pathLength, moveDelay, stepAndActionTicks;
    private int actionSubState, age, gender, rangedSkill, meleeSkill, throwRange, hitPoints, satiety;
    private int energy, portrait, action, animation, animationTick, thought, carriedItem, carriedAmount;
    private int carryCapacity, savedAction, pathBlock, sightRadius, idleTicks, appearance;
    private int projectileSubX, projectileSubY, projectileVelocityX, projectileVelocityY, projectileStep;
    private int projectileFlightSteps, orderMode, assignedTask, weapon, ageTicks, maxHitPoints, nameIndex;
    private int harvesting, weaponsmithing, cooking, djing, witchcraft, building, scouting;
    private int projectileFacing, runBonus, projectileDamageFloor, status, diseaseExposure;
    private int protectionTicks, pairLink, buildingSlot, offsetX, offsetY;

    /// <summary>The couple link bits of the original: the slot + 1 in bits 1..6 and the delayed death in bit 7 of a byte.</summary>
    private const int OriginalLinkMask = 0x7F, OriginalDeathBit = 0x80;

    /// <summary>The couple link bits of a match (no limit on couple slots): the slot + 1 in bits 1..29, the delayed death in bit 30.</summary>
    private const int WideLinkMask = 0x3FFFFFFF, WideDeathBit = 0x40000000;

    private bool wideLinks;

    internal Unit(int id, int tribe, bool wideLinks = false)
    {
        Id = id;
        Tribe = tribe;
        this.wideLinks = wideLinks;
    }

    /// <summary>Slot number, unique in the world; the original numbers tribe t's units t*50+1.. .</summary>
    public int Id { get; }

    /// <summary>The tribe the slot belongs to (for good).</summary>
    public int Tribe { get; }

    /// <summary>
    /// The slot's place among its tribe's unit slots (<see cref="GameWorld.UnitsOf"/>): 0 for the
    /// tribe's first slot, whose unit the original treats as the tribe's leader.
    /// </summary>
    public int SlotInTribe { get; internal init; }

    /// <summary>Whether the slot holds a unit (alive or dying).</summary>
    public bool InUse => HitPoints != 0;

    // ---- position and movement ----

    /// <summary>Cell column.</summary>
    public int X { get; set; }

    /// <summary>Cell row.</summary>
    public int Y { get; set; }

    /// <summary>Pixel offset from the cell centre while stepping (a cell is 22 wide).</summary>
    public int OffsetX { get => offsetX; set => offsetX = Wrap.Signed8(value); }

    /// <summary>Pixel offset from the cell centre while stepping (a cell is 16 high).</summary>
    public int OffsetY { get => offsetY; set => offsetY = Wrap.Signed8(value); }

    /// <summary>Whether the unit stands exactly on its cell centre.</summary>
    public bool IsCentred => OffsetX == 0 && OffsetY == 0;

    /// <summary>Direction 0..7 (east, then clockwise; odd values are diagonals).</summary>
    public int Facing { get => facing; set => facing = Wrap.Signed8(value); }

    /// <summary>Index of the next waypoint on <see cref="Path"/>.</summary>
    public int PathIndex { get => pathIndex; set => pathIndex = Wrap.Signed8(value); }

    /// <summary>Number of waypoints on <see cref="Path"/>; 0 when there is no route.</summary>
    public int PathLength { get => pathLength; set => pathLength = Wrap.Signed8(value); }

    /// <summary>The route being walked (cells), valid up to <see cref="PathLength"/>.</summary>
    public UnitPath Path { get; } = new();

    /// <summary>Walking slowness (ticks per cell class); larger is slower.</summary>
    public int MoveDelay { get => moveDelay; set => moveDelay = Wrap.Signed8(value); }

    /// <summary>Speed-up while running (0 or 3), subtracted from <see cref="MoveDelay"/>.</summary>
    public int RunBonus { get => runBonus; set => runBonus = Wrap.Signed8(value); }

    /// <summary>
    /// Two 4-bit counters: low bits count movement ticks within a step, high bits count action
    /// ticks; the high counter wraps from 15 to 0 as in the original.
    /// </summary>
    public int StepAndActionTicks { get => stepAndActionTicks; set => stepAndActionTicks = Wrap.Signed8(value); }

    /// <summary>Movement ticks within the current step (low 4 bits of <see cref="StepAndActionTicks"/>).</summary>
    public int StepTicks => StepAndActionTicks & 0x0F;

    /// <summary>Action ticks of the current action cycle (high 4 bits of <see cref="StepAndActionTicks"/>).</summary>
    public int ActionTicks => (StepAndActionTicks & 0xFF) >> 4;

    /// <summary>Blocked-route counter, or a marker value while swapping cells with a tribe mate.</summary>
    public int PathBlock { get => pathBlock; set => pathBlock = Wrap.Signed8(value); }

    /// <summary>Ticks since the last decision; decisions are taken every (MoveDelay - RunBonus) ticks.</summary>
    public int IdleTicks { get => idleTicks; set => idleTicks = Wrap.Signed8(value); }

    // ---- what the unit is doing ----

    /// <summary>Current action (index into the action table).</summary>
    public int Action { get => action; set => action = Wrap.Signed8(value); }

    /// <summary>Long-term task (an action number).</summary>
    public int AssignedTask { get => assignedTask; set => assignedTask = Wrap.Signed8(value); }

    /// <summary>Action to resume after an interruption.</summary>
    public int SavedAction { get => savedAction; set => savedAction = Wrap.Signed8(value); }

    /// <summary>Progress of the current work action; produces a result at the action's threshold.</summary>
    public int WorkProgress { get => workProgress; set => workProgress = Wrap.Signed8(value); }

    /// <summary>Animation being played (read as unsigned 0..255).</summary>
    public int Animation { get => animation; set => animation = Wrap.Signed8(value); }

    /// <summary><see cref="Animation"/> as the unsigned number the rules compare.</summary>
    public int AnimationNumber => Animation & 0xFF;

    /// <summary>Frame counter of <see cref="Animation"/>.</summary>
    public int AnimationTick { get => animationTick; set => animationTick = Wrap.Signed8(value); }

    /// <summary>
    /// Sub-state of entering, being in or leaving a building and variants of the action
    /// (-3 inside, -2 at the door, -1 stop at the next centre, 0 normal, positive variants).
    /// </summary>
    public int ActionSubState { get => actionSubState; set => actionSubState = Wrap.Signed8(value); }

    /// <summary>The entity the current action works on (meaning depends on the action's target kind).</summary>
    public int TargetIndex { get; set; }

    /// <summary>The cell content the target was picked from, as a cell code.</summary>
    public int TargetCellCode { get; set; }

    /// <summary>Entity that receives the effect of each action cycle (usually the unit itself).</summary>
    public int EffectRecipient { get; set; }

    /// <summary>Destination cell of the current movement.</summary>
    public int DestinationX { get; set; }

    /// <summary>Destination cell of the current movement.</summary>
    public int DestinationY { get; set; }

    /// <summary>Place of the long-term task, where the unit goes back to.</summary>
    public int TaskSiteX { get; set; }

    /// <summary>Place of the long-term task, where the unit goes back to.</summary>
    public int TaskSiteY { get; set; }

    /// <summary>How the unit takes orders.</summary>
    public int OrderMode { get => orderMode; set => orderMode = Wrap.Signed8(value); }

    /// <summary>
    /// Facing to take on arriving at a building entrance (-100 none), or the production order a
    /// crafting action belongs to.
    /// </summary>
    public int EntranceFacingOrOrder { get; set; }

    /// <summary>Work position inside a building (0..7), -1 none, or a negative overlay effect.</summary>
    public int BuildingSlot { get => buildingSlot; set => buildingSlot = Wrap.Signed8(value); }

    /// <summary>
    /// Next unit in the building's occupant list (0 = last), or while outdoors the trap or object
    /// code of the cell being crossed.
    /// </summary>
    public int NextOccupantOrCellObject { get; set; }

    /// <summary>The hut the unit sleeps in; 0 none (freed), -1 never assigned.</summary>
    public int Home { get; set; }

    // ---- body and needs ----

    /// <summary>Age in years.</summary>
    public int Age { get => age; set => age = Wrap.Signed8(value); }

    /// <summary>Sub-year counter; the unit ages when it reaches the tribe's threshold.</summary>
    public int AgeTicks { get => ageTicks; set => ageTicks = Wrap.Signed8(value); }

    /// <summary>0 male, 1 female.</summary>
    public int Gender { get => gender; set => gender = Wrap.Signed8(value); }

    /// <summary>Hit points; negative while the death animation plays; 0 frees the slot.</summary>
    public int HitPoints { get => hitPoints; set => hitPoints = Wrap.Signed8(value); }

    /// <summary>Maximum hit points.</summary>
    public int MaxHitPoints { get => maxHitPoints; set => maxHitPoints = Wrap.Signed8(value); }

    /// <summary>How full the unit is (0 starving .. 100).</summary>
    public int Satiety { get => satiety; set => satiety = Wrap.Signed8(value); }

    /// <summary>How rested the unit is (0 exhausted .. 99).</summary>
    public int Energy { get => energy; set => energy = Wrap.Signed8(value); }

    /// <summary>Mood (0..99); below 10 the unit ignores orders.</summary>
    public int Happiness { get => happiness; set => happiness = Wrap.Signed8(value); }

    /// <summary>Disease exposure; reaching 30 makes the unit sick.</summary>
    public int DiseaseExposure { get => diseaseExposure; set => diseaseExposure = Wrap.Signed8(value); }

    /// <summary>Ticks the protection spell has lasted.</summary>
    public int ProtectionTicks { get => protectionTicks; set => protectionTicks = Wrap.Signed8(value); }

    /// <summary>Status bits (<see cref="UnitStatus"/>).</summary>
    public UnitStatus Status { get => (UnitStatus)(status & 0xFF); set => status = Wrap.Signed8((int)value); }

    /// <summary>
    /// Couple membership: bit 0 role, bits 1..6 couple slot + 1, bit 7 delayed death (in a match the
    /// slot takes bits 1..29 and the delayed death bit 30: see <see cref="CoupleLinkMask"/>).
    /// </summary>
    public int PairLink { get => pairLink; set => pairLink = wideLinks ? value : Wrap.Signed8(value); }

    /// <summary>The bits of <see cref="PairLink"/> naming the role and the couple slot + 1.</summary>
    public int CoupleLinkMask => wideLinks ? WideLinkMask : OriginalLinkMask;

    /// <summary>The bit of <see cref="PairLink"/> marking a delayed death.</summary>
    public int DelayedDeathBit => wideLinks ? WideDeathBit : OriginalDeathBit;

    /// <summary>Gives the slot a match's couple link bits, keeping the couple and the delayed death it has.</summary>
    internal void WidenLinks()
    {
        if (wideLinks)
        {
            return;
        }
        int link = pairLink & 0xFF;
        wideLinks = true;
        pairLink = (link & OriginalLinkMask) | ((link & OriginalDeathBit) != 0 ? WideDeathBit : 0);
    }

    /// <summary>Head and body variant bits used for drawing.</summary>
    public int Appearance { get => appearance; set => appearance = Wrap.Signed8(value); }

    /// <summary>Portrait picture shown on the unit panel.</summary>
    public int Portrait { get => portrait; set => portrait = Wrap.Signed8(value); }

    /// <summary>Name string number.</summary>
    public int NameIndex { get => nameIndex; set => nameIndex = Wrap.Signed8(value); }

    /// <summary>Thought shown above the unit (-1 none, negative cool-down, else an icon).</summary>
    public int Thought { get => thought; set => thought = Wrap.Signed8(value); }

    // ---- skills and equipment ----

    /// <summary>Melee fighting skill.</summary>
    public int MeleeSkill { get => meleeSkill; set => meleeSkill = Wrap.Signed8(value); }

    /// <summary>Ranged fighting skill.</summary>
    public int RangedSkill { get => rangedSkill; set => rangedSkill = Wrap.Signed8(value); }

    /// <summary>Scouting skill (hiding, spotting traps).</summary>
    public int ScoutingSkill { get => scouting; set => scouting = Wrap.Signed8(value); }

    /// <summary>Gathering skill.</summary>
    public int HarvestingSkill { get => harvesting; set => harvesting = Wrap.Signed8(value); }

    /// <summary>Weapon making skill.</summary>
    public int WeaponsmithingSkill { get => weaponsmithing; set => weaponsmithing = Wrap.Signed8(value); }

    /// <summary>Cooking skill.</summary>
    public int CookingSkill { get => cooking; set => cooking = Wrap.Signed8(value); }

    /// <summary>Entertaining skill.</summary>
    public int DjingSkill { get => djing; set => djing = Wrap.Signed8(value); }

    /// <summary>Witchcraft skill.</summary>
    public int WitchcraftSkill { get => witchcraft; set => witchcraft = Wrap.Signed8(value); }

    /// <summary>Building skill.</summary>
    public int BuildingSkill { get => building; set => building = Wrap.Signed8(value); }

    /// <summary>Range of thrown weapons in cells.</summary>
    public int ThrowRange { get => throwRange; set => throwRange = Wrap.Signed8(value); }

    /// <summary>Sight radius in cells.</summary>
    public int SightRadius { get => sightRadius; set => sightRadius = Wrap.Signed8(value); }

    /// <summary>Equipped weapon kind.</summary>
    public int Weapon { get => weapon; set => weapon = Wrap.Signed8(value); }

    /// <summary>What the unit carries (0 nothing).</summary>
    public int CarriedItem { get => carriedItem; set => carriedItem = Wrap.Signed8(value); }

    /// <summary>How much of <see cref="CarriedItem"/> it carries.</summary>
    public int CarriedAmount { get => carriedAmount; set => carriedAmount = Wrap.Signed8(value); }

    /// <summary>How much it can carry.</summary>
    public int CarryCapacity { get => carryCapacity; set => carryCapacity = Wrap.Signed8(value); }

    // ---- projectile in flight ----

    /// <summary>Cell of the unit's projectile in flight.</summary>
    public int ProjectileX { get; set; }

    /// <summary>Cell of the unit's projectile in flight.</summary>
    public int ProjectileY { get; set; }

    /// <summary>Pixel offset of the projectile within its cell.</summary>
    public int ProjectileSubX { get => projectileSubX; set => projectileSubX = Wrap.Signed8(value); }

    /// <summary>Pixel offset of the projectile within its cell.</summary>
    public int ProjectileSubY { get => projectileSubY; set => projectileSubY = Wrap.Signed8(value); }

    /// <summary>Projectile speed in pixels per step.</summary>
    public int ProjectileVelocityX { get => projectileVelocityX; set => projectileVelocityX = Wrap.Signed8(value); }

    /// <summary>Projectile speed in pixels per step.</summary>
    public int ProjectileVelocityY { get => projectileVelocityY; set => projectileVelocityY = Wrap.Signed8(value); }

    /// <summary>Steps the projectile has flown.</summary>
    public int ProjectileStep { get => projectileStep; set => projectileStep = Wrap.Signed8(value); }

    /// <summary>Steps the flight takes; 0 when no projectile is flying.</summary>
    public int ProjectileFlightSteps { get => projectileFlightSteps; set => projectileFlightSteps = Wrap.Signed8(value); }

    /// <summary>Direction the projectile was thrown in (picks its picture).</summary>
    public int ProjectileFacing { get => projectileFacing; set => projectileFacing = Wrap.Signed8(value); }

    /// <summary>Lowest damage roll of the projectile, in tenths.</summary>
    public int ProjectileDamageFloor { get => projectileDamageFloor; set => projectileDamageFloor = Wrap.Signed8(value); }

    /// <summary>Saves, loads or hashes every value of the slot (see <see cref="StateExchange"/>).</summary>
    internal void Exchange(StateExchange s)
    {
        if (s.Format < StateExchange.SlotsFormat && s.Value(Tribe) != Tribe)
        {
            throw new System.IO.InvalidDataException($"unit slot {Id} of tribe {Tribe} saved as another tribe's");
        }
        X = s.Value(X);
        Y = s.Value(Y);
        OffsetX = s.Value(OffsetX);
        OffsetY = s.Value(OffsetY);
        Facing = s.Value(Facing);
        PathIndex = s.Value(PathIndex);
        PathLength = s.Value(PathLength);
        MoveDelay = s.Value(MoveDelay);
        RunBonus = s.Value(RunBonus);
        StepAndActionTicks = s.Value(StepAndActionTicks);
        PathBlock = s.Value(PathBlock);
        IdleTicks = s.Value(IdleTicks);
        Action = s.Value(Action);
        AssignedTask = s.Value(AssignedTask);
        SavedAction = s.Value(SavedAction);
        WorkProgress = s.Value(WorkProgress);
        Animation = s.Value(Animation);
        AnimationTick = s.Value(AnimationTick);
        ActionSubState = s.Value(ActionSubState);
        TargetIndex = s.Value(TargetIndex);
        TargetCellCode = s.Value(TargetCellCode);
        EffectRecipient = s.Value(EffectRecipient);
        DestinationX = s.Value(DestinationX);
        DestinationY = s.Value(DestinationY);
        TaskSiteX = s.Value(TaskSiteX);
        TaskSiteY = s.Value(TaskSiteY);
        OrderMode = s.Value(OrderMode);
        EntranceFacingOrOrder = s.Value(EntranceFacingOrOrder);
        BuildingSlot = s.Value(BuildingSlot);
        NextOccupantOrCellObject = s.Value(NextOccupantOrCellObject);
        Home = s.Value(Home);
        Age = s.Value(Age);
        AgeTicks = s.Value(AgeTicks);
        Gender = s.Value(Gender);
        HitPoints = s.Value(HitPoints);
        MaxHitPoints = s.Value(MaxHitPoints);
        Satiety = s.Value(Satiety);
        Energy = s.Value(Energy);
        Happiness = s.Value(Happiness);
        DiseaseExposure = s.Value(DiseaseExposure);
        ProtectionTicks = s.Value(ProtectionTicks);
        Status = (UnitStatus)s.Value((int)Status);
        PairLink = s.Value(PairLink);
        Appearance = s.Value(Appearance);
        Portrait = s.Value(Portrait);
        NameIndex = s.Value(NameIndex);
        Thought = s.Value(Thought);
        MeleeSkill = s.Value(MeleeSkill);
        RangedSkill = s.Value(RangedSkill);
        ScoutingSkill = s.Value(ScoutingSkill);
        HarvestingSkill = s.Value(HarvestingSkill);
        WeaponsmithingSkill = s.Value(WeaponsmithingSkill);
        CookingSkill = s.Value(CookingSkill);
        DjingSkill = s.Value(DjingSkill);
        WitchcraftSkill = s.Value(WitchcraftSkill);
        BuildingSkill = s.Value(BuildingSkill);
        ThrowRange = s.Value(ThrowRange);
        SightRadius = s.Value(SightRadius);
        Weapon = s.Value(Weapon);
        CarriedItem = s.Value(CarriedItem);
        CarriedAmount = s.Value(CarriedAmount);
        CarryCapacity = s.Value(CarryCapacity);
        ProjectileX = s.Value(ProjectileX);
        ProjectileY = s.Value(ProjectileY);
        ProjectileSubX = s.Value(ProjectileSubX);
        ProjectileSubY = s.Value(ProjectileSubY);
        ProjectileVelocityX = s.Value(ProjectileVelocityX);
        ProjectileVelocityY = s.Value(ProjectileVelocityY);
        ProjectileStep = s.Value(ProjectileStep);
        ProjectileFlightSteps = s.Value(ProjectileFlightSteps);
        ProjectileFacing = s.Value(ProjectileFacing);
        ProjectileDamageFloor = s.Value(ProjectileDamageFloor);
        Path.Exchange(s);
    }

    /// <summary>Returns the value of <paramref name="skill"/>.</summary>
    public int Skill(UnitSkill skill) => skill switch
    {
        UnitSkill.Melee => MeleeSkill,
        UnitSkill.Ranged => RangedSkill,
        UnitSkill.Scouting => ScoutingSkill,
        UnitSkill.Harvesting => HarvestingSkill,
        UnitSkill.Weaponsmithing => WeaponsmithingSkill,
        UnitSkill.Cooking => CookingSkill,
        UnitSkill.Djing => DjingSkill,
        UnitSkill.Witchcraft => WitchcraftSkill,
        UnitSkill.Building => BuildingSkill,
        _ => throw new ArgumentOutOfRangeException(nameof(skill), skill, null),
    };
}

/// <summary>Skills a unit can have.</summary>
public enum UnitSkill
{
    /// <summary>Melee fighting.</summary>
    Melee,

    /// <summary>Ranged fighting.</summary>
    Ranged,

    /// <summary>Scouting.</summary>
    Scouting,

    /// <summary>Gathering.</summary>
    Harvesting,

    /// <summary>Weapon making.</summary>
    Weaponsmithing,

    /// <summary>Cooking.</summary>
    Cooking,

    /// <summary>Entertaining.</summary>
    Djing,

    /// <summary>Witchcraft.</summary>
    Witchcraft,

    /// <summary>Building.</summary>
    Building,
}

/// <summary>Status bits of a unit.</summary>
[Flags]
public enum UnitStatus
{
    /// <summary>No status.</summary>
    None = 0,

    /// <summary>No enemy has seen the unit since the last check.</summary>
    Undetected = 0x01,

    /// <summary>Hiding.</summary>
    Hidden = 0x02,

    /// <summary>Sick.</summary>
    Diseased = 0x04,

    /// <summary>Under the protection spell (takes half damage).</summary>
    Protected = 0x08,

    /// <summary>Going to cast a spell.</summary>
    SpellPending = 0x10,

    /// <summary>Called to the rally point.</summary>
    RallyPending = 0x20,

    /// <summary>Member of the army.</summary>
    Mustered = 0x40,

    /// <summary>Hunting one animal on a one-off order.</summary>
    OneOffHunt = 0x80,
}

/// <summary>A unit's route: up to <see cref="Capacity"/> waypoints.</summary>
public sealed class UnitPath
{
    /// <summary>Most waypoints a route holds.</summary>
    public const int Capacity = 80;

    private readonly int[] x = new int[Capacity];
    private readonly int[] y = new int[Capacity];

    /// <summary>Column of waypoint <paramref name="i"/>.</summary>
    public int XAt(int i) => x[i];

    /// <summary>Row of waypoint <paramref name="i"/>.</summary>
    public int YAt(int i) => y[i];

    /// <summary>Sets waypoint <paramref name="i"/>.</summary>
    public void Set(int i, int column, int row)
    {
        x[i] = column;
        y[i] = row;
    }

    /// <summary>Saves, loads or hashes the waypoints (a hasher takes all columns, then all rows, at once).</summary>
    internal void Exchange(StateExchange s)
    {
        if (s is StateHasher hasher)
        {
            hasher.MixAll(x);
            hasher.MixAll(y);
            return;
        }
        for (int i = 0; i < Capacity; i++)
        {
            x[i] = s.Value(x[i]);
            y[i] = s.Value(y[i]);
        }
    }

    /// <summary>Copies all waypoints from <paramref name="other"/>.</summary>
    public void CopyFrom(UnitPath other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Array.Copy(other.x, x, Capacity);
        Array.Copy(other.y, y, Capacity);
    }
}
