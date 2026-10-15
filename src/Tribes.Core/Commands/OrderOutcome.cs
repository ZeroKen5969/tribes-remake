namespace Tribes.Core.Commands;

/// <summary>
/// What became of an order. Anything but <see cref="Applied"/> means the world was left as it was;
/// every peer reaches the same outcome for the same order.
/// </summary>
public enum OrderOutcome
{
    /// <summary>The order was carried out (single units may still have refused, as in the game).</summary>
    Applied = 0,

    /// <summary>The payload is not an order of this game version.</summary>
    Malformed,

    /// <summary>No such tribe.</summary>
    UnknownTribe,

    /// <summary>The tribe cannot give this order now (no such state for it, nothing awaited, no scene shown...).</summary>
    NotAvailable,

    /// <summary>A unit is outside the unit slots, of another tribe, or (where one unit is ordered) not alive.</summary>
    UnknownUnit,

    /// <summary>A building is outside the building slots, of another tribe, or not standing.</summary>
    UnknownBuilding,

    /// <summary>The building is of the wrong kind, or not in the state, for the order.</summary>
    WrongBuilding,

    /// <summary>A cell is outside the playable map, or outside the building it must be on.</summary>
    OutOfMap,

    /// <summary>The game would not offer this choice now (not in the menu, not made at this level, out of stock, out of range).</summary>
    NotOffered,

    /// <summary>Nothing could be placed there: the footprint is not free or not in view, or no slot is free.</summary>
    Blocked,
}
