using Tribes.Core.Content;

namespace Tribes.Core.Commands;

/// <summary>
/// Something a player orders their tribe to do. Orders are values: the user interface builds one,
/// the session sends it (<see cref="GameOrderCodec"/>) and every peer applies it with
/// <see cref="OrderProcessor"/> in the same tick, before the world update. The tribe is the one
/// of the slot that sent the order; it is never read from the payload. Units and buildings are
/// given by slot number.
/// </summary>
/// <param name="Tribe">The tribe giving the order.</param>
public abstract record GameOrder(int Tribe);

/// <summary>
/// Units walk to a cell and take on what is there (attack, gather, enter, hunt...). A unit sent
/// to the cell it is already heading for, or sent with <paramref name="Hurry"/>, runs; otherwise
/// it stops running. Unhappy units refuse unless the tribe is called to arms.
/// </summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Units">The tribe's units ordered.</param>
/// <param name="Cell">Where they go.</param>
/// <param name="Target">
/// The cell code (<see cref="Maps.CellCode"/>) of what the player aimed at: what fills the cell,
/// or a unit or animal standing there. A creature that is still alive stays the target even if
/// it moved; for anything else the rules take what fills the cell when the order runs.
/// </param>
/// <param name="Hurry">Whether the player asked them to run (a double press).</param>
public sealed record MoveUnits(int Tribe, UnitSet Units, GridPoint Cell, int Target, bool Hurry) : GameOrder(Tribe);

/// <summary>
/// A building of <paramref name="Kind"/> (0..11, offered by the build menu) is planned with its
/// top-left cell at <paramref name="Origin"/>: its construction site is placed if the footprint
/// is free and in view.
/// </summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Kind">The building kind to build.</param>
/// <param name="Origin">The footprint's top-left cell.</param>
public sealed record PlaceBuilding(int Tribe, int Kind, GridPoint Origin) : GameOrder(Tribe);

/// <summary>A trap is ordered from an armoury; its makers set it at <paramref name="Cell"/>.</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Armoury">The armoury's building slot.</param>
/// <param name="TrapKind">The trap's building kind (20 net, 21 spring, 22 pit), made at the armoury's level.</param>
/// <param name="Cell">Where the trap goes.</param>
public sealed record PlaceTrap(int Tribe, int Armoury, int TrapKind, GridPoint Cell) : GameOrder(Tribe);

/// <summary>A building of the tribe is torn down.</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Building">The building slot.</param>
public sealed record DemolishBuilding(int Tribe, int Building) : GameOrder(Tribe);

/// <summary>What <see cref="OrderBuildingWork"/> asks of a building.</summary>
public enum BuildingWork
{
    /// <summary>Upgrade it to the next level (while the level is below the kind's unlocked level).</summary>
    Upgrade = 0,

    /// <summary>Repair it (while it is damaged).</summary>
    Repair = 1,

    /// <summary>Call off its upgrade.</summary>
    StopUpgrade = 2,

    /// <summary>Call off its repair.</summary>
    StopRepair = 3,
}

/// <summary>A building of the tribe is upgraded or repaired, or its upgrade or repair is called off.</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Building">The building slot.</param>
/// <param name="Work">What to do.</param>
public sealed record OrderBuildingWork(int Tribe, int Building, BuildingWork Work) : GameOrder(Tribe);

/// <summary>A unit inside one of the tribe's buildings (its operator or an occupant) is sent out.</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Building">The building slot.</param>
/// <param name="Unit">The unit slot.</param>
public sealed record EvictUnit(int Tribe, int Building, int Unit) : GameOrder(Tribe);

/// <summary>A unit picked in the roster comes out of the building it is in, so it can be given orders.</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Unit">The unit slot.</param>
public sealed record CallOutUnit(int Tribe, int Unit) : GameOrder(Tribe);

/// <summary>After the chief died, the player names an adult of the tribe the new chief (it also comes out of its building).</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Unit">The unit slot.</param>
public sealed record AppointChief(int Tribe, int Unit) : GameOrder(Tribe);

/// <summary>The tribe-wide calls to arms.</summary>
public enum ArmyCall
{
    /// <summary>The fighters gather at a point.</summary>
    Rally = 0,

    /// <summary>Everyone goes to a point.</summary>
    AllOut = 1,
}

/// <summary>
/// The tribe is called to arms at <paramref name="Point"/>. A rally needs neither call to be on;
/// an all-out call replaces a rally.
/// </summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Call">Which call.</param>
/// <param name="Point">The cell they go to.</param>
public sealed record CallArmy(int Tribe, ArmyCall Call, GridPoint Point) : GameOrder(Tribe);

/// <summary>A call to arms is called off (a rally only while no all-out call is on).</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Call">Which call.</param>
public sealed record CancelArmyCall(int Tribe, ArmyCall Call) : GameOrder(Tribe);

/// <summary>How many of a weapon an armoury is still to make.</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Armoury">The armoury's building slot.</param>
/// <param name="Product">The weapon product (0..5) the armoury makes at its level.</param>
/// <param name="Count">Items still to make, 0..<see cref="OrderMenus.MaxArmouryCount"/>.</param>
public sealed record SetArmouryOrder(int Tribe, int Armoury, int Product, int Count) : GameOrder(Tribe);

/// <summary>Units go to a store and change to a weapon the tribe has in stock.</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Units">The tribe's units ordered.</param>
/// <param name="Store">The store's building slot.</param>
/// <param name="Weapon">The weapon kind (0 club ... 5 tomahawk).</param>
/// <param name="Cell">A cell of the store's footprint they walk to.</param>
public sealed record FetchWeapon(int Tribe, UnitSet Units, int Store, int Weapon, GridPoint Cell) : GameOrder(Tribe);

/// <summary>Units carrying goods take them to a store (at least one of them must carry something).</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Units">The tribe's units ordered.</param>
/// <param name="Store">The store's building slot.</param>
/// <param name="Cell">A cell of the store's footprint they walk to.</param>
public sealed record DeliverToStore(int Tribe, UnitSet Units, int Store, GridPoint Cell) : GameOrder(Tribe);

/// <summary>A sanctuary is asked to cast a spell on an adult of the tribe (spells 0 healing, 1 protection, 2 love potion).</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Sanctuary">The sanctuary's building slot.</param>
/// <param name="Spell">The spell, one the sanctuary's level offers.</param>
/// <param name="Unit">The unit slot it is cast on.</param>
public sealed record CastSpell(int Tribe, int Sanctuary, int Spell, int Unit) : GameOrder(Tribe);

/// <summary>A sanctuary is asked to cast a spell on a place (spell 3, the disease).</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Sanctuary">The sanctuary's building slot.</param>
/// <param name="Spell">The spell, one the sanctuary's level offers.</param>
/// <param name="Cell">Where it is cast.</param>
public sealed record CastSpellAt(int Tribe, int Sanctuary, int Spell, GridPoint Cell) : GameOrder(Tribe);

/// <summary>What <see cref="TendNeed"/> sends a unit for.</summary>
public enum UnitNeed
{
    /// <summary>To the nearest kitchen that can take it, to eat.</summary>
    Food = 0,

    /// <summary>To the nearest hut that can take it, to rest.</summary>
    Rest = 1,
}

/// <summary>A unit is sent to eat or rest (as a hungry or tired unit's hint offers).</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Unit">The unit slot.</param>
/// <param name="Need">What for.</param>
public sealed record TendNeed(int Tribe, int Unit, UnitNeed Need) : GameOrder(Tribe);

/// <summary>The player switches hints on or off (hint cool-downs only run while they are on).</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Enabled">Whether hints are on.</param>
public sealed record SetHints(int Tribe, bool Enabled) : GameOrder(Tribe);

/// <summary>The player opened the help of the action bar (a tutorial step may wait for that).</summary>
/// <param name="Tribe">The tribe giving the order.</param>
public sealed record ViewedHelp(int Tribe) : GameOrder(Tribe);

/// <summary>The player goes on in the story scene shown: its next step, or its end.</summary>
/// <param name="Tribe">The tribe giving the order.</param>
public sealed record AdvanceScene(int Tribe) : GameOrder(Tribe);

/// <summary>
/// The tribe invites the people's tribe <paramref name="Invitee"/> into an alliance (endless worlds:
/// the tribe needs a finished alliance building, the invitee's player must be playing).
/// </summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Invitee">The tribe invited.</param>
public sealed record InviteAlly(int Tribe, int Invitee) : GameOrder(Tribe);

/// <summary>The tribe accepts or turns down the open invitation of <paramref name="Inviter"/>.</summary>
/// <param name="Tribe">The tribe giving the order (the invited one).</param>
/// <param name="Inviter">The tribe that invited it.</param>
/// <param name="Accept">Whether it accepts (then both are allies at once).</param>
public sealed record AnswerAlliance(int Tribe, int Inviter, bool Accept) : GameOrder(Tribe);

/// <summary>The tribe leaves its alliance with <paramref name="Ally"/>: the two are enemies again.</summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Ally">Its ally.</param>
public sealed record LeaveAlliance(int Tribe, int Ally) : GameOrder(Tribe);

/// <summary>
/// The tribe gives <paramref name="Ally"/> goods from its store counters (both need a finished
/// alliance building): up to <paramref name="Amount"/> of <paramref name="Good"/>, as much as the
/// ally can take up to the goods cap; the tribe loses only what arrives.
/// </summary>
/// <param name="Tribe">The tribe giving the order.</param>
/// <param name="Ally">The ally receiving.</param>
/// <param name="Good">The store counter (food, wood, stones, bones, meals or a weapon).</param>
/// <param name="Amount">How much, 1 up to what the tribe has.</param>
public sealed record TransferGoods(int Tribe, int Ally, Tribes.Core.World.TribeCounter Good, int Amount) : GameOrder(Tribe);
