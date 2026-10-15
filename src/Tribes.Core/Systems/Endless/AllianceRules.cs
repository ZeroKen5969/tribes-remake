using System;
using System.Linq;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// What tribes of an endless world may do about alliances: a tribe with a finished alliance
/// building invites another people's tribe whose player is playing (not away, not given up, not
/// out); the invited tribe accepts or turns it down while the invitation is open; an ally may leave
/// the alliance at any time; allies give each other goods while both have an alliance building.
/// Fog is never shared. Other worlds have no alliances made in the game.
/// </summary>
public static class AllianceRules
{
    /// <summary>Whether <paramref name="tribe"/> has a finished alliance building.</summary>
    public static bool HasAllianceBuilding(GameWorld world, int tribe) => AllianceBuilding.Stands(world, tribe);

    /// <summary>Whether <paramref name="tribe"/> is a people's tribe whose player is playing now (and it is still in).</summary>
    public static bool IsPlaying(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return IsTribe(world, tribe) && world.Tribes[tribe].Controller.Kind == TribeControl.Human && world.Tribes[tribe].OutAtFrame < 0;
    }

    /// <summary>Whether an invitation between <paramref name="a"/> and <paramref name="b"/> (either way) is open.</summary>
    public static bool HasOpenInvitation(GameWorld world, int a, int b)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Endless != null
            && world.Endless.Invitations.Any(i => (i.From == a && i.To == b) || (i.From == b && i.To == a));
    }

    /// <summary>Whether <paramref name="from"/> may invite <paramref name="to"/> into an alliance now.</summary>
    public static bool CanInvite(GameWorld world, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Endless != null && from != to && IsPlaying(world, from) && IsPlaying(world, to)
            && HasAllianceBuilding(world, from) && !world.Relations.AreAllies(from, to) && !HasOpenInvitation(world, from, to);
    }

    /// <summary>Whether <paramref name="tribe"/> may answer the open invitation of <paramref name="inviter"/>.</summary>
    public static bool CanAnswer(GameWorld world, int tribe, int inviter)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Endless != null && IsPlaying(world, tribe) && IsTribe(world, inviter) && world.Tribes[inviter].OutAtFrame < 0
            && world.Endless.Invitations.Any(i => i.From == inviter && i.To == tribe);
    }

    /// <summary>Whether <paramref name="tribe"/> may leave its alliance with <paramref name="ally"/>.</summary>
    public static bool CanLeave(GameWorld world, int tribe, int ally)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Endless != null && tribe != ally && IsTribe(world, tribe) && IsTribe(world, ally) && world.Relations.AreAllies(tribe, ally);
    }

    /// <summary>Whether <paramref name="from"/> may give goods to <paramref name="to"/> now: allies, both still in, both with an alliance building.</summary>
    public static bool CanTransfer(GameWorld world, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Endless != null && from != to && IsTribe(world, from) && IsTribe(world, to)
            && world.Relations.AreAllies(from, to) && world.Tribes[from].OutAtFrame < 0 && world.Tribes[to].OutAtFrame < 0
            && HasAllianceBuilding(world, from) && HasAllianceBuilding(world, to);
    }

    /// <summary>Whether <paramref name="good"/> is a store counter allies may give.</summary>
    public static bool IsTransferable(GameWorld world, TribeCounter good)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Content.Endless is { } rules && rules.Alliance.Goods.Contains(good);
    }

    private static bool IsTribe(GameWorld world, int tribe) => tribe >= 0 && tribe < world.Tribes.Count;
}
