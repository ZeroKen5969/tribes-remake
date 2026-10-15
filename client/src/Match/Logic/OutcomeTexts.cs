using Tribes.Core.Commands;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>The text key of the note shown when the rules refuse one of the player's orders.</summary>
public static class OutcomeTexts
{
    /// <summary>
    /// The key for <paramref name="outcome"/>. A building or trap that cannot be placed is refused
    /// because the ground is not free and seen, and, where tribes have a building limit (the
    /// campaign), also when the limit is reached; without a limit the note does not mention one.
    /// </summary>
    public static string Key(OrderOutcome outcome, bool buildingLimit) =>
        outcome == OrderOutcome.Blocked && !buildingLimit ? "hud.outcome.Blocked.unlimited" : "hud.outcome." + outcome;
}
