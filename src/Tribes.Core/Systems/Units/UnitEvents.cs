using Tribes.Core.Simulation;

namespace Tribes.Core.Systems.Units;

/// <summary>
/// A unit went into a building or came out of one; the screen drops it from the player's
/// selection and selection groups (and closes the panel it had open).
/// </summary>
/// <param name="Unit">The unit slot.</param>
public sealed record UnitDeselected(int Unit) : GameEvent;
