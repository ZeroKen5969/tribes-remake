using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Vision;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Entities;

/// <summary>
/// What a new unit is born with, packed as the original passes it: bits 0..2 a skill level (sight,
/// fighting and walking speed grow with it), bits 3..4 a scouting level, bit 5 gifted (civil skills
/// roll higher), bit 6 armed (it gets a random weapon).
/// </summary>
/// <param name="Bits">The packed value (the original keeps it in a signed byte).</param>
internal readonly record struct UnitTraits(int Bits)
{
    private const int SkillMask = 0x07, ScoutingShift = 3, ScoutingMask = 0x03, GiftedBit = 0x20, ArmedBit = 0x40;

    /// <summary>No skills, no weapon.</summary>
    public static UnitTraits None => new(0);

    /// <summary>The skill level 0..7.</summary>
    public int SkillLevel => Bits & SkillMask;

    /// <summary>The scouting level 0..3.</summary>
    public int ScoutingLevel => (Bits >> ScoutingShift) & ScoutingMask;

    /// <summary>Whether the civil skills roll higher.</summary>
    public bool Gifted => (Bits & GiftedBit) != 0;

    /// <summary>Whether the unit gets a random weapon.</summary>
    public bool Armed => (Bits & ArmedBit) != 0;
}

/// <summary>
/// Units coming into the world: a new unit in the first free slot of its tribe with rolled skills,
/// needs, looks, gender and name, and the portrait and name a unit shows.
/// </summary>
internal sealed class UnitSpawning
{
    /// <summary>Weapon kind of a unit without a weapon.</summary>
    public const int BareHands = WeaponNumbers.BareHands;

    private const int Roll = 0xFF, NameRoll = 0xFFFF;
    private const int BaseSight = 4, BaseThrowRange = 3, SpawnFacing = 3, NoHome = -1, NoThought = -1;
    private const int BaseNeed = 50, PlayerNeedBonus = 25, NeedRoll = 25;
    private const int ChildMoveDelay = 8, UnskilledMoveDelay = 8, AdultMoveDelay = 9, ChildBaseHitPoints = 5, ChildHitPointRoll = 10;
    private const int AdultBaseHitPoints = 40, TutorialEnemyHitPoints = 20, MaleHitPointBonus = 10, AdultHitPointRoll = 15;
    private const int CivilChoices = 3, BaseCivilSkill = 2, PlainSkill = 1, CivilSpread = 2, GiftedSpread = 3;
    private const int FightingSpread = 2, SkillPerLevel = 2, ScoutingChoices = 3, ScoutingPerLevel = 3, BaseScouting = 1;
    private const int AllMale = 0, AllFemale = 1, Male = 0, Female = 1;
    private const int BaseCarryCapacity = 2, CarrySpread = 2, HeadVariants = 3, BodyVariants = 4, BodyShift = 2, BodyRollMask = 0x0F;
    private const int PlayerWeaponKinds = 4, ComputerWeaponKinds = 6;
    private const int StoryLeaders = 2, StoryNamedUnits = 3, RandomNames = 58, NamesPerGender = 60;
    private const int StoryLeaderName = -2, NoName = -1;
    private const int AgeClasses = 5, ChildClass = 0, YoungClass = 1, ElderClass = 2, EnemyClass = 3, EnemyLeaderClass = 4;

    private readonly GameWorld world;
    private readonly HintSystem hints;
    private readonly SightSystem sight;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitSpawning(GameWorld world, HintSystem hints, SightSystem sight)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.sight = sight ?? throw new ArgumentNullException(nameof(sight));
    }

    /// <summary>
    /// A new unit of <paramref name="tribe"/> aged <paramref name="age"/> at <paramref name="cell"/>
    /// (it takes the cell if the cell is empty), or null when the tribe has no free slot. Fields the
    /// birth does not set keep the values of the slot's last unit, as in the original.
    /// </summary>
    public Unit? Spawn(int tribe, GridPoint cell, int age, UnitTraits traits)
    {
        Unit? unit = world.FreeUnitSlot(tribe);
        if (unit is null)
        {
            return null;
        }
        Place(unit, cell, age, traits);
        RollFightingAndNeeds(unit, traits);
        RollCivilSkills(unit, traits);
        ClearTasks(unit);
        RollAppearanceAndGender(unit);
        GrowUp(unit, traits);
        Arm(unit, traits);
        PickName(unit);
        PickPortrait(unit);
        if (world.Map.CellAt(cell).IsEmpty)
        {
            world.Map.SetCell(cell, MapCell.UnitAt(unit.Id));
        }
        CountBirth(unit, cell);
        return unit;
    }

    /// <summary>
    /// Picks <paramref name="unit"/>'s portrait from the range of its gender and age class (child,
    /// young, elder, or for the computer's tribe an enemy or the enemy leader).
    /// </summary>
    public void PickPortrait(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int ageClass;
        if (TribeRoles.IsComputer(world, unit.Tribe))
        {
            ageClass = unit.SlotInTribe == 0 ? EnemyLeaderClass : EnemyClass;
        }
        else
        {
            ageClass = unit.Age < UnitAges.Adult ? ChildClass : unit.Age < UnitAges.Elder ? YoungClass : ElderClass;
        }
        PortraitRange range = world.Content.Rules.Units.Portraits[(unit.Gender * AgeClasses) + ageClass];
        unit.Portrait = range.First + world.Random.NextMasked(Roll, range.Count);
    }

    /// <summary>
    /// Picks <paramref name="unit"/>'s name: the player's first units keep their story names, the
    /// others draw one of their gender's names; the computer's story leaders and other units have
    /// no name of their own.
    /// </summary>
    public void PickName(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int number = unit.SlotInTribe + 1;
        if (TribeRoles.IsComputer(world, unit.Tribe))
        {
            unit.NameIndex = number <= StoryLeaders ? StoryLeaderName : NoName;
            return;
        }
        int name = number > StoryNamedUnits ? 1 + world.Random.NextMasked(NameRoll, RandomNames) : number;
        unit.NameIndex = (name % NamesPerGender) + (NamesPerGender * unit.Gender) - 1;
    }

    private static void Place(Unit unit, GridPoint cell, int age, UnitTraits traits)
    {
        unit.X = unit.TaskSiteX = unit.DestinationX = cell.X;
        unit.Y = unit.TaskSiteY = unit.DestinationY = cell.Y;
        unit.OffsetX = unit.OffsetY = 0;
        unit.PathLength = 0;
        unit.StepAndActionTicks = 0;
        unit.SightRadius = BaseSight + traits.SkillLevel;
        unit.Facing = SpawnFacing;
        unit.ActionSubState = 0;
        unit.Age = age;
    }

    private void RollFightingAndNeeds(Unit unit, UnitTraits traits)
    {
        GameRandom random = world.Random;
        unit.RangedSkill = (traits.SkillLevel * SkillPerLevel) + random.NextMasked(Roll, FightingSpread);
        unit.MeleeSkill = (traits.SkillLevel * SkillPerLevel) + random.NextMasked(Roll, FightingSpread);
        unit.ThrowRange = BaseThrowRange + traits.SkillLevel;
        int need = BaseNeed + (TribeRoles.IsPlayer(world, unit.Tribe) ? PlayerNeedBonus : 0);
        unit.Satiety = need + random.NextMasked(Roll, NeedRoll);
        unit.Energy = need + random.NextMasked(Roll, NeedRoll);
        unit.Happiness = need + random.NextMasked(Roll, NeedRoll);
    }

    private void RollCivilSkills(Unit unit, UnitTraits traits)
    {
        GameRandom random = world.Random;
        int spread = CivilSpread + (traits.Gifted ? GiftedSpread : 0);
        int first = random.NextMasked(Roll, CivilChoices);
        int second = random.NextMasked(Roll, CivilChoices);
        int witchBonus = TribeRoles.IsComputer(world, unit.Tribe) ? 1 : 0;
        unit.HarvestingSkill = first == 0 ? BaseCivilSkill + random.NextMasked(Roll, spread) : PlainSkill;
        unit.WeaponsmithingSkill = first == 1 ? BaseCivilSkill + random.NextMasked(Roll, spread) : PlainSkill;
        unit.DjingSkill = first == 2 ? BaseCivilSkill + random.NextMasked(Roll, spread) : PlainSkill;
        unit.CookingSkill = second == 0 ? BaseCivilSkill + random.NextMasked(Roll, spread) : PlainSkill;
        unit.WitchcraftSkill = second == 1 ? BaseCivilSkill + witchBonus + random.NextMasked(Roll, spread) : PlainSkill + witchBonus;
        unit.BuildingSkill = second == 2 ? BaseCivilSkill + random.NextMasked(Roll, spread) : PlainSkill;
        unit.ScoutingSkill = BaseScouting + (traits.ScoutingLevel * ScoutingPerLevel) + random.NextMasked(Roll, ScoutingChoices);
    }

    private void ClearTasks(Unit unit)
    {
        unit.AssignedTask = ActionNumbers.Idle;
        unit.Home = NoHome;
        unit.CarriedItem = 0;
        unit.CarriedAmount = 0;
        unit.CarryCapacity = BaseCarryCapacity + world.Random.NextMasked(Roll, CarrySpread);
        unit.EffectRecipient = unit.Id;
        unit.NextOccupantOrCellObject = 0;
        unit.BuildingSlot = -1;
        unit.TargetCellCode = CellCode.OfUnit(unit.Id);
        unit.PathBlock = 0;
    }

    /// <summary>Rolls the looks, then picks the gender (balancing the tribe unless the story wants one) and counts it.</summary>
    private void RollAppearanceAndGender(Unit unit)
    {
        int head = world.Random.NextMasked(Roll, HeadVariants);
        int body = world.Random.NextMasked(BodyRollMask, BodyVariants);
        unit.Appearance = (body << BodyShift) | head;
        TribeState tribe = world.Tribes[unit.Tribe];
        int mode = world.SpawnGenderMode;
        bool male = (tribe[TribeCounter.Males] <= tribe[TribeCounter.Females] || mode == AllMale) && mode != AllFemale;
        unit.Gender = male ? Male : Female;
        tribe[male ? TribeCounter.Males : TribeCounter.Females]++;
    }

    private void GrowUp(Unit unit, UnitTraits traits)
    {
        GameRandom random = world.Random;
        if (unit.Age < UnitAges.Adult)
        {
            unit.Action = ActionNumbers.ChildIdle;
            unit.Animation = AnimationNumbers.ChildStanding;
            unit.MaxHitPoints = unit.HitPoints = ChildBaseHitPoints + random.NextMasked(Roll, ChildHitPointRoll);
            unit.MoveDelay = ChildMoveDelay;
            return;
        }
        unit.Action = ActionNumbers.Idle;
        unit.Animation = AnimationNumbers.FirstStanding;
        bool weakEnemy = world.Mission == MissionNumbers.Tutorial && TribeRoles.IsComputer(world, unit.Tribe);
        int hitPoints = (weakEnemy ? TutorialEnemyHitPoints : AdultBaseHitPoints) + (unit.Gender == Male ? MaleHitPointBonus : 0)
            + random.NextMasked(Roll, AdultHitPointRoll);
        unit.MaxHitPoints = unit.HitPoints = Wrap.Signed8(hitPoints);
        unit.MoveDelay = traits.SkillLevel == 0 ? UnskilledMoveDelay : AdultMoveDelay - traits.SkillLevel;
    }

    /// <summary>Gives the weapon (by traits and mission) and clears the target, thought, status, couple and disease.</summary>
    private void Arm(Unit unit, UnitTraits traits)
    {
        unit.TargetIndex = unit.Id;
        unit.Weapon = BareHands;
        if (traits.Armed)
        {
            int kinds = TribeRoles.IsComputer(world, unit.Tribe) ? ComputerWeaponKinds : PlayerWeaponKinds;
            unit.Weapon = world.Random.NextMasked(Roll, kinds);
        }
        if (world.Mission == MissionNumbers.War)
        {
            unit.Weapon = WeaponNumbers.Club;
        }
        if (world.Mission is >= MissionNumbers.FirstWithoutStones and <= MissionNumbers.LastWithoutStones
            && unit.Weapon == WeaponNumbers.Stone)
        {
            unit.Weapon = WeaponNumbers.StoneAxe;
        }
        unit.Thought = NoThought;
        unit.Status = UnitStatus.None;
        unit.PairLink = 0;
        unit.DiseaseExposure = 0;
    }

    private void CountBirth(Unit unit, GridPoint cell)
    {
        bool player = TribeRoles.IsPlayer(world, unit.Tribe);
        if (!world.Script.Active)
        {
            TribeState tribe = world.Tribes[unit.Tribe];
            tribe[TribeCounter.Population]++;
            if (player)
            {
                hints.Notice(unit.Tribe, EntityNotices.UnitBorn, cell.X, cell.Y, unit.Id);
            }
            tribe[TribeCounter.Born]++;
        }
        if (player)
        {
            sight.RevealDisc(unit.Tribe, FogLayer.Visible, unit.X, unit.Y, unit.SightRadius);
        }
    }
}
