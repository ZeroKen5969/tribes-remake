using System;
using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Core.Simulation;

/// <summary>
/// Something the rules tell the presentation layer about: a message, a hint, a sound, a story
/// scene, a visual effect. The rules never call the user interface; they add events here and the
/// host shows them. Systems define their own event records deriving from this one.
/// </summary>
public abstract record GameEvent;

/// <summary>
/// All rule systems of one world, wired to each other. Systems are created once per world and keep
/// no state of their own beyond what the world holds, so a world can be saved and loaded freely.
/// Each group of systems adds its entry point in its own partial file (RuleSystems.Group.cs).
/// </summary>
public sealed partial class RuleSystems
{
    private readonly List<GameEvent> events = new();

    /// <summary>Creates the systems for <paramref name="world"/>.</summary>
    public RuleSystems(GameWorld world)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>The world the systems work on.</summary>
    public GameWorld World { get; }

    /// <summary>Events raised since <see cref="TakeEvents"/> was last called.</summary>
    public IReadOnlyList<GameEvent> PendingEvents => events;

    /// <summary>Raises <paramref name="gameEvent"/> for the presentation layer.</summary>
    public void Raise(GameEvent gameEvent) => events.Add(gameEvent ?? throw new ArgumentNullException(nameof(gameEvent)));

    /// <summary>The events raised so far, in order; the list is emptied.</summary>
    public IReadOnlyList<GameEvent> TakeEvents()
    {
        var taken = events.ToArray();
        events.Clear();
        return taken;
    }
}
