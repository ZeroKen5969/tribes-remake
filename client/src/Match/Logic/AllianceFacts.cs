using System;
using System.Collections.Generic;
using Tribes.Client.Ui.Logic;
using Tribes.Core.Systems.Endless;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>
/// What the tribes panel shows of an endless world for the player of <c>local</c> (-1 watching):
/// each tribe's standing, the open invitations both ways, and what the rules let this player do
/// about it now (<see cref="AllianceRules"/>).
/// </summary>
public static class AllianceFacts
{
    /// <summary>The facts of every tribe of <paramref name="world"/>, in tribe order.</summary>
    public static IReadOnlyList<TribeFacts> For(GameWorld world, int local)
    {
        ArgumentNullException.ThrowIfNull(world);
        var facts = new List<TribeFacts>(world.Tribes.Count);
        IReadOnlyList<AllianceInvitation> invitations = world.Endless?.Invitations ?? Array.Empty<AllianceInvitation>();
        for (int t = 0; t < world.Tribes.Count; t++)
        {
            TribeState tribe = world.Tribes[t];
            bool other = local >= 0 && local < world.Tribes.Count && t != local;
            bool invitedByUs = false, invitesUs = false;
            foreach (AllianceInvitation invitation in invitations)
            {
                invitedByUs |= other && invitation.From == local && invitation.To == t;
                invitesUs |= other && invitation.From == t && invitation.To == local;
            }
            TribeControl kind = tribe.Controller.Kind;
            facts.Add(new TribeFacts(t, local >= 0 && t == local, other && world.Relations.AreAllies(local, t),
                tribe.OutAtFrame >= 0 || kind == TribeControl.Forfeited, kind == TribeControl.Computer, kind == TribeControl.Absent,
                invitedByUs, invitesUs,
                other && AllianceRules.CanInvite(world, local, t), other && AllianceRules.CanAnswer(world, local, t),
                other && AllianceRules.CanLeave(world, local, t), other && AllianceRules.CanTransfer(world, local, t)));
        }
        return facts;
    }
}
