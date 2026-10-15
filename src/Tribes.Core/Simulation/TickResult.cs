using System.Collections.Generic;
using Tribes.Core.Commands;

namespace Tribes.Core.Simulation;

/// <summary>What kind of tick ran.</summary>
public enum TickKind
{
    /// <summary>Normal play: the orders, then one world frame (none when an order started a scene or showed a pausing hint).</summary>
    Play,

    /// <summary>A story scene was shown: the orders (a tap goes on), then one display frame of the scene, which may run a staged world frame.</summary>
    Scene,

    /// <summary>The host held the game: only the orders were carried out.</summary>
    Paused,

    /// <summary>The mission is over: nothing changes any more.</summary>
    Over,
}

/// <summary>What one tick did.</summary>
/// <param name="Tick">The tick's number (from 0).</param>
/// <param name="Kind">What kind of tick it was.</param>
/// <param name="WorldAdvanced">Whether a world frame ran.</param>
/// <param name="Outcomes">What became of each order, in the input's order.</param>
/// <param name="Events">What the presentation is told about, in the order it happened.</param>
/// <param name="Victory">The mission's result when it was decided on this tick; null otherwise.</param>
public sealed record TickResult(long Tick, TickKind Kind, bool WorldAdvanced, IReadOnlyList<OrderOutcome> Outcomes,
    IReadOnlyList<GameEvent> Events, bool? Victory);
