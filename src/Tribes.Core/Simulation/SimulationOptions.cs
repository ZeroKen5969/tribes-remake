namespace Tribes.Core.Simulation;

/// <summary>Choices a simulation keeps for its whole game.</summary>
public sealed record SimulationOptions
{
    /// <summary>
    /// Whether a hint shown stops the world for the rest of its tick, as in the single-player game
    /// (the host then pauses until the player closes the hint). Off for matches nobody pauses.
    /// </summary>
    public bool HintsPauseGame { get; init; } = true;
}
