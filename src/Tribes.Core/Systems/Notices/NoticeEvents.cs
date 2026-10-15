using Tribes.Core.Simulation;

namespace Tribes.Core.Systems.Notices;

/// <summary>
/// A hint for the player. The original game stops the world while a hint is on screen and goes on
/// when the player closes it; a host that wants the same should pause until then.
/// </summary>
/// <param name="Hint">The hint number (row of <c>GameRules.Story.Hints</c>).</param>
/// <param name="Subject">The unit the hint is about (0 none).</param>
/// <param name="Target">What the hint points at (a cell code or a unit, depending on the hint).</param>
public sealed record HintEvent(int Hint, int Subject, int Target) : GameEvent
{
    /// <summary>Whether the original pauses the game while a hint is shown (it always does).</summary>
    public static bool PausesGame => true;

    /// <summary>The tribe whose player the hint is for (tribe 0 in the original).</summary>
    public int Tribe { get; init; }
}

/// <summary>
/// A notice in the message list: a unit was born or died, a building was finished or lost, a
/// building cannot be built, an enemy was seen...
/// </summary>
/// <param name="Kind">The notice kind (the original's message number).</param>
/// <param name="X">The cell column it is about (or a first value).</param>
/// <param name="Y">The cell row it is about (or a second value).</param>
/// <param name="Value">A third value (a unit or building, depending on the kind).</param>
public sealed record NoticeEvent(int Kind, int X, int Y, int Value) : GameEvent
{
    /// <summary>The tribe whose player's message list the notice is for (tribe 0 in the original).</summary>
    public int Tribe { get; init; }
}
