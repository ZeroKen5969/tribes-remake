using System;

namespace Tribes.Core.World;

/// <summary>Who plays a tribe.</summary>
public enum TribeControl
{
    /// <summary>A human player who is in the game and gives orders.</summary>
    Human,

    /// <summary>The computer player.</summary>
    Computer,

    /// <summary>A human player who left or lost the connection: no orders and no AI until they return.</summary>
    Absent,

    /// <summary>A human player who gave up: no orders and no AI for the rest of the game.</summary>
    Forfeited,
}

/// <summary>
/// The controller of a tribe. Human-ruled tribes (human, absent or forfeited) keep the rules the
/// original applies to the player's tribe; only a present human gives orders and only the computer
/// runs the AI.
/// </summary>
/// <param name="Kind">Who plays the tribe.</param>
/// <param name="Slot">The player slot of a human-ruled tribe (kept while absent or forfeited); -1 for the computer.</param>
/// <param name="Difficulty">The computer's difficulty (0 easy, 1 hard); null plays at the world's difficulty.</param>
public readonly record struct TribeController(TribeControl Kind, int Slot, int? Difficulty)
{
    /// <summary>A present human player in <paramref name="slot"/>.</summary>
    public static TribeController Human(int slot) => new(TribeControl.Human, CheckSlot(slot), null);

    /// <summary>The computer at <paramref name="difficulty"/> (null: the world's difficulty).</summary>
    public static TribeController Computer(int? difficulty = null) =>
        difficulty is < 0 ? throw new ArgumentOutOfRangeException(nameof(difficulty)) : new(TribeControl.Computer, -1, difficulty);

    /// <summary>The player of <paramref name="slot"/> is away.</summary>
    public static TribeController Absent(int slot) => new(TribeControl.Absent, CheckSlot(slot), null);

    /// <summary>The player of <paramref name="slot"/> gave up.</summary>
    public static TribeController Forfeited(int slot) => new(TribeControl.Forfeited, CheckSlot(slot), null);

    /// <summary>Whether the tribe plays by the rules the original gives the player's tribe.</summary>
    public bool IsHumanRuled => Kind != TribeControl.Computer;

    /// <summary>Whether the tribe takes player orders now.</summary>
    public bool TakesOrders => Kind == TribeControl.Human;

    /// <summary>Whether the computer player runs the tribe.</summary>
    public bool RunsAi => Kind == TribeControl.Computer;

    private static int CheckSlot(int slot) =>
        slot >= 0 ? slot : throw new ArgumentOutOfRangeException(nameof(slot), slot, "player slots are 0 or more");
}
