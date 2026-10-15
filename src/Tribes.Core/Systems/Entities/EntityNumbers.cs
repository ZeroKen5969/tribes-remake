namespace Tribes.Core.Systems.Entities;

/// <summary>
/// Action numbers the entity rules refer to by name. Actions are rows of the action table
/// (<c>GameRules.Actions.Actions</c>); most behaviour comes from there, these few are named because
/// the rules treat them specially.
/// </summary>
internal static class ActionNumbers
{
    /// <summary>Highest of the ten basic attacks (0..4 melee, 5..9 ranged); they get the weapon's ranged offset.</summary>
    public const int LastBasicAttack = 9;

    /// <summary>Hunting an animal in melee.</summary>
    public const int HuntMelee = 2;

    /// <summary>Hunting an animal with a thrown weapon.</summary>
    public const int HuntRanged = 7;

    /// <summary>First of the gathering actions (10..16, one per carried item).</summary>
    public const int FirstGathering = 10;

    /// <summary>Gathering meat from a carcass.</summary>
    public const int GatherCarcass = 12;

    /// <summary>Fishing; the fisher stands on the shore next to the fishing spot.</summary>
    public const int Fishing = 13;

    /// <summary>Gathering bones; also the last of the gathering actions.</summary>
    public const int GatherBones = 16;

    /// <summary>Last of the gathering actions.</summary>
    public const int LastGathering = 16;

    /// <summary>What a child does when it has nothing else to do.</summary>
    public const int ChildIdle = 17;

    /// <summary>First of the actions that deliver a carried item to the store.</summary>
    public const int FirstDelivery = 19;

    /// <summary>Last of the actions that deliver a carried item to the store.</summary>
    public const int LastDelivery = 26;

    /// <summary>The witch doctor at work in the sanctuary.</summary>
    public const int WitchDoctor = 57;

    /// <summary>Staying inside a building that the unit only visits.</summary>
    public const int StayInside = 59;

    /// <summary>The long-term hunting task.</summary>
    public const int HuntingTask = 60;

    /// <summary>What an adult does when it has nothing else to do.</summary>
    public const int Idle = 69;

    /// <summary>A builder sitting at its site until material arrives.</summary>
    public const int WaitForMaterial = 78;

    /// <summary>First of the three interruption actions (resting, waiting, resuming); they keep what the unit carries.</summary>
    public const int FirstInterruption = 83;

    /// <summary>Last of the three interruption actions.</summary>
    public const int LastInterruption = 85;

    /// <summary>Leaving the building the unit is in.</summary>
    public const int LeaveBuilding = 87;

    /// <summary>Guarding from a tower.</summary>
    public const int TowerGuard = 92;

    /// <summary>Shooting from a tower.</summary>
    public const int TowerShot = 93;

    /// <summary>Guarding against animals from a tower.</summary>
    public const int TowerAnimalGuard = 106;

    /// <summary>Capturing an enemy child.</summary>
    public const int CaptureChild = 107;

    /// <summary>First of the couple actions (the couple's walk to the hut and what follows).</summary>
    public const int FirstCoupleAction = 109;

    /// <summary>Last of the couple actions (109..111).</summary>
    public const int LastCoupleAction = 111;
}

/// <summary>Animation numbers the entity rules refer to by name.</summary>
internal static class AnimationNumbers
{
    /// <summary>Walking.</summary>
    public const int Walk = 0;

    /// <summary>Walking with a load (or a stone to throw).</summary>
    public const int CarryWalk = 1;

    /// <summary>Crawling while hidden.</summary>
    public const int Crawl = 5;

    /// <summary>A child walking.</summary>
    public const int ChildWalk = 6;

    /// <summary>Running.</summary>
    public const int Run = 7;

    /// <summary>The kinds of walking; standing animation = <see cref="FirstStanding"/> + walking animation modulo this.</summary>
    public const int WalkKinds = 7;

    /// <summary>The first standing animation (standing still after <see cref="Walk"/>).</summary>
    public const int FirstStanding = 8;

    /// <summary>A child standing.</summary>
    public const int ChildStanding = 14;

    /// <summary>The witch doctor receiving a patient.</summary>
    public const int WitchDoctorAtWork = 105;

    /// <summary>First of the animations 113..116 of a builder waiting for material (sitting down, waiting, standing up).</summary>
    public const int FirstMaterialWait = 113;

    /// <summary>Last of the animations of a builder waiting for material.</summary>
    public const int LastMaterialWait = 116;

    /// <summary>Highest animation number of the walking animations.</summary>
    public const int LastWalking = 7;

    /// <summary>
    /// A unit inside a building getting up to go to another workplace (as the unsigned animation
    /// number); it is not sent out again while it plays.
    /// </summary>
    public const int ChangingWorkplace = 148;
}

/// <summary>Building kind numbers the entity rules refer to by name (rows of <c>GameRules.Buildings.Kinds</c>).</summary>
internal static class BuildingKindNumbers
{
    /// <summary>The armoury: each armoury has a production queue.</summary>
    public const int Armoury = 0;

    /// <summary>A hut: units live in it; it adds living space.</summary>
    public const int Hut = 2;

    /// <summary>The chief's hut.</summary>
    public const int ChiefsHut = 3;

    /// <summary>The store, where carried goods are delivered.</summary>
    public const int Store = 6;

    /// <summary>The kitchen.</summary>
    public const int Kitchen = 4;

    /// <summary>The disco.</summary>
    public const int Disco = 7;

    /// <summary>The sanctuary.</summary>
    public const int Sanctuary = 8;

    /// <summary>The tower: its guards' sight sets its range.</summary>
    public const int Tower = 11;

    /// <summary>Number of real building kinds (0..11); the site kinds follow.</summary>
    public const int PlannedKinds = 12;

    /// <summary>First construction site kind.</summary>
    public const int FirstSite = 12;

    /// <summary>Last construction site kind.</summary>
    public const int LastSite = 19;

    /// <summary>First trap kind (traps do not take building slots).</summary>
    public const int FirstTrap = 20;

    /// <summary>Last trap kind.</summary>
    public const int LastTrap = 22;

    /// <summary>The first kind an endless world adds after the original's (the alliance building).</summary>
    public const int FirstEndlessKind = 26;

    /// <summary>Whether <paramref name="kind"/> is a construction site.</summary>
    public static bool IsSite(int kind) => kind >= FirstSite && kind <= LastSite;

    /// <summary>Whether <paramref name="kind"/> is a trap.</summary>
    public static bool IsTrap(int kind) => kind >= FirstTrap && kind <= LastTrap;
}

/// <summary>Values of <see cref="World.Unit.OrderMode"/>.</summary>
internal static class OrderModes
{
    /// <summary>The unit decides for itself.</summary>
    public const int Free = 0;

    /// <summary>Keeps an order it was given until it gets stuck.</summary>
    public const int Keep = 1;

    /// <summary>Walks to its destination first, then picks what to do there.</summary>
    public const int Walk = 2;

    /// <summary>Does its current action whatever happens; cleared when the action ends.</summary>
    public const int Forced = 3;
}

/// <summary>Age limits of units.</summary>
internal static class UnitAges
{
    /// <summary>Units of this age or older are adults.</summary>
    public const int Adult = 12;

    /// <summary>Units of this age or older get an elder's portrait.</summary>
    public const int Elder = 35;
}

/// <summary>Missions the entity rules treat specially.</summary>
internal static class MissionNumbers
{
    /// <summary>The first mission (the tutorial): the computer's adults are weaker.</summary>
    public const int Tutorial = 0;

    /// <summary>The mission where the computer's units run when the first objective is reached.</summary>
    public const int Kidnapping = 2;

    /// <summary>The war mission: everyone fights with clubs and nobody takes up work.</summary>
    public const int War = 3;

    /// <summary>First of the missions without stones as weapons (they become stone axes).</summary>
    public const int FirstWithoutStones = 4;

    /// <summary>Last of the missions without stones as weapons.</summary>
    public const int LastWithoutStones = 5;
}

/// <summary>How an order ends (the mode passed to <see cref="UnitOrders.FinishOrder"/>).</summary>
internal static class FinishModes
{
    /// <summary>The unit chose the order itself.</summary>
    public const int OwnChoice = 0;

    /// <summary>A kept order.</summary>
    public const int Kept = 1;

    /// <summary>A walking or forced order.</summary>
    public const int Walked = 2;

    /// <summary>A couple's move (the couple is not dissolved).</summary>
    public const int CoupleMove = 3;
}

/// <summary>Action categories (<c>ActionDefinition.Category</c>) the entity rules name.</summary>
internal static class ActionCategoryNumbers
{
    /// <summary>Free or unassigned: idling, moving, visiting.</summary>
    public const int Free = 0;
}

/// <summary>Weapon kinds the entity rules name (rows of <c>GameRules.Weapons.Weapons</c>).</summary>
internal static class WeaponNumbers
{
    /// <summary>A club.</summary>
    public const int Club = 0;

    /// <summary>A pile of stones: units carrying it walk as with a load.</summary>
    public const int Stone = 1;

    /// <summary>A stone axe.</summary>
    public const int StoneAxe = 3;

    /// <summary>No weapon.</summary>
    public const int BareHands = 9;
}
