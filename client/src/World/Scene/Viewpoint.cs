namespace Tribes.Client.World.Scene;

/// <summary>
/// Whose eyes a view uses: a player's (its own tribe, which in a match also sees what its allies
/// see) or a spectator's (what every human player sees). <see cref="Tribe"/> is the home tribe: the
/// player's, or for a spectator the tribe the screen starts at.
/// </summary>
/// <param name="Tribe">The home tribe.</param>
/// <param name="Spectating">Whether the view is a spectator's.</param>
public readonly record struct Viewpoint(int Tribe, bool Spectating)
{
    /// <summary>The view of the player of <paramref name="tribe"/> (with its allies' sight in a match).</summary>
    public static Viewpoint Player(int tribe) => new(tribe, false);

    /// <summary>A spectator's view (every human player's sight), starting at <paramref name="homeTribe"/>.</summary>
    public static Viewpoint Spectator(int homeTribe) => new(homeTribe, true);
}
