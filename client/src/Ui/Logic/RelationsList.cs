using System;
using System.Collections.Generic;

namespace Tribes.Client.Ui.Logic;

/// <summary>What the player can do about another tribe in the tribes panel.</summary>
public enum RelationAction
{
    /// <summary>Accept its invitation into an alliance.</summary>
    Accept,

    /// <summary>Turn its invitation down.</summary>
    Decline,

    /// <summary>Invite it into an alliance.</summary>
    Invite,

    /// <summary>Give it goods (allies).</summary>
    Give,

    /// <summary>Leave the alliance with it.</summary>
    Leave,
}

/// <summary>What the panel knows of a tribe (from the rules, for this player).</summary>
/// <param name="Tribe">The tribe.</param>
/// <param name="IsLocal">This player's tribe.</param>
/// <param name="IsAlly">An ally of this player's tribe.</param>
/// <param name="IsOut">Wiped out or given up.</param>
/// <param name="IsComputer">Played by the computer.</param>
/// <param name="IsAbsent">Its player is away (offline).</param>
/// <param name="InvitedByUs">This player's invitation to it is open.</param>
/// <param name="InvitesUs">Its invitation to this player is open.</param>
/// <param name="CanInvite">The rules let this player invite it now.</param>
/// <param name="CanAnswer">The rules let this player answer its invitation now.</param>
/// <param name="CanLeave">The rules let this player leave the alliance with it now.</param>
/// <param name="CanGive">The rules let this player give it goods now.</param>
public sealed record TribeFacts(int Tribe, bool IsLocal, bool IsAlly, bool IsOut, bool IsComputer, bool IsAbsent,
    bool InvitedByUs, bool InvitesUs, bool CanInvite, bool CanAnswer, bool CanLeave, bool CanGive);

/// <summary>A row of the tribes panel.</summary>
/// <param name="Tribe">The tribe.</param>
/// <param name="StatusKey">The text key of how it stands.</param>
/// <param name="Actions">What the player can do about it now, in button order.</param>
public sealed record RelationRow(int Tribe, string StatusKey, IReadOnlyList<RelationAction> Actions);

/// <summary>
/// The tribes panel of an endless world: this player's tribe first, then allies, tribes inviting
/// this one, the other people's tribes, computer tribes and the tribes that are out (each group in
/// tribe order); each row says how the tribe stands and offers what the rules allow now.
/// </summary>
public static class RelationsList
{
    /// <summary>The rows for <paramref name="tribes"/>.</summary>
    public static IReadOnlyList<RelationRow> Build(IEnumerable<TribeFacts> tribes)
    {
        ArgumentNullException.ThrowIfNull(tribes);
        var sorted = new List<TribeFacts>(tribes);
        sorted.Sort((a, b) => Group(a) != Group(b) ? Group(a).CompareTo(Group(b)) : a.Tribe.CompareTo(b.Tribe));
        var rows = new List<RelationRow>(sorted.Count);
        foreach (TribeFacts tribe in sorted)
        {
            rows.Add(new RelationRow(tribe.Tribe, StatusKey(tribe), Actions(tribe)));
        }
        return rows;
    }

    /// <summary>The note above the rows: a tribe without a finished alliance hall cannot invite nor give (watchers get none).</summary>
    public static string? HallNote(bool hasHall, bool spectating) => hasHall || spectating ? null : "relations.need_hall";

    private static int Group(TribeFacts t) =>
        t.IsLocal ? 0 : t.IsOut ? 5 : t.IsAlly ? 1 : t.InvitesUs ? 2 : t.IsComputer ? 4 : 3;

    private static string StatusKey(TribeFacts t) =>
        t.IsLocal ? "relations.status.you"
        : t.IsOut ? "relations.status.out"
        : t.IsAlly ? "relations.status.ally"
        : t.InvitesUs ? "relations.status.invites_you"
        : t.InvitedByUs ? "relations.status.invited_by_you"
        : t.IsComputer ? "relations.status.computer"
        : t.IsAbsent ? "relations.status.offline"
        : "relations.status.enemy";

    private static IReadOnlyList<RelationAction> Actions(TribeFacts t)
    {
        var actions = new List<RelationAction>();
        if (t.IsLocal || t.IsOut)
        {
            return actions;
        }
        if (t.InvitesUs && t.CanAnswer)
        {
            actions.Add(RelationAction.Accept);
            actions.Add(RelationAction.Decline);
        }
        if (t.CanInvite)
        {
            actions.Add(RelationAction.Invite);
        }
        if (t.CanGive)
        {
            actions.Add(RelationAction.Give);
        }
        if (t.CanLeave)
        {
            actions.Add(RelationAction.Leave);
        }
        return actions;
    }
}
