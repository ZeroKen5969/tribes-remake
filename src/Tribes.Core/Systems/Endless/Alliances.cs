using System;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// Alliances made in an endless world (<see cref="AllianceRules"/>): invitations, answers, leaving,
/// goods given between allies, invitations lapsing and alliances ending when a side is out. When two tribes become allies, their units
/// that were attacking each other stop; from then on neither side can hurt the other (the cell
/// rules read an ally as neither friend nor foe).
/// </summary>
internal sealed class Alliances
{
    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly Action<GameEvent> raise;

    /// <summary>The alliances of <paramref name="world"/>.</summary>
    public Alliances(GameWorld world, UnitActions actions, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
    }

    /// <summary><paramref name="from"/> invites <paramref name="to"/>; false (and nothing changes) when it may not.</summary>
    public bool Invite(int from, int to)
    {
        if (!AllianceRules.CanInvite(world, from, to))
        {
            return false;
        }
        world.Endless!.Invite(new AllianceInvitation(from, to, world.Frame));
        raise(new AllianceInvited(from, to));
        return true;
    }

    /// <summary><paramref name="tribe"/> answers the invitation of <paramref name="inviter"/>; false when there is none it may answer.</summary>
    public bool Answer(int tribe, int inviter, bool accept)
    {
        if (!AllianceRules.CanAnswer(world, tribe, inviter))
        {
            return false;
        }
        world.Endless!.RemoveInvitations(i => i.From == inviter && i.To == tribe);
        if (!accept)
        {
            raise(new AllianceDeclined(inviter, tribe));
            return true;
        }
        world.Relations.SetAlliance(inviter, tribe, true);
        world.Endless.RemoveInvitations(i => (i.From == tribe && i.To == inviter));
        StandDown(inviter, tribe);
        StandDown(tribe, inviter);
        raise(new AllianceFormed(inviter, tribe));
        return true;
    }

    /// <summary><paramref name="tribe"/> leaves its alliance with <paramref name="ally"/>; false when they are not allies.</summary>
    public bool Leave(int tribe, int ally)
    {
        if (!AllianceRules.CanLeave(world, tribe, ally))
        {
            return false;
        }
        world.Relations.SetAlliance(tribe, ally, false);
        raise(new AllianceEnded(Math.Min(tribe, ally), Math.Max(tribe, ally), tribe));
        return true;
    }

    /// <summary>
    /// <paramref name="from"/> gives <paramref name="ally"/> up to <paramref name="amount"/> of
    /// <paramref name="good"/> from its store counter; the ally gets at most up to the goods cap.
    /// Returns what arrived, or -1 when the transfer is not allowed (nothing changes).
    /// </summary>
    public int Transfer(int from, int ally, TribeCounter good, int amount)
    {
        if (!AllianceRules.CanTransfer(world, from, ally) || !AllianceRules.IsTransferable(world, good)
            || amount <= 0 || amount > world.Tribes[from][good])
        {
            return -1;
        }
        int room = Math.Max(0, world.Content.Endless!.Alliance.GoodsCap - world.Tribes[ally][good]);
        int arrived = Math.Min(amount, room);
        world.Tribes[from][good] -= arrived;
        world.Tribes[ally][good] += arrived;
        raise(new GoodsTransferred(from, ally, good, arrived));
        return arrived;
    }

    /// <summary>
    /// Alliances with a tribe that is out (wiped out or given up) end, as if it had left
    /// (<see cref="AllianceEnded.By"/> is the tribe that is out).
    /// </summary>
    public void EndWithTribesOut()
    {
        if (world.Relations.Alliances.Count == 0)
        {
            return;
        }
        foreach ((int low, int high) in world.Relations.Alliances.ToArray())
        {
            int gone = world.Tribes[low].OutAtFrame >= 0 ? low : world.Tribes[high].OutAtFrame >= 0 ? high : -1;
            if (gone >= 0)
            {
                world.Relations.SetAlliance(low, high, false);
                raise(new AllianceEnded(low, high, gone));
            }
        }
    }

    /// <summary>Invitations past their time, or that can no longer be answered, lapse.</summary>
    public void Lapse()
    {
        EndlessState state = world.Endless!;
        int frames = world.Content.Endless!.Alliance.InvitationFrames;
        for (int i = state.Invitations.Count - 1; i >= 0; i--)
        {
            AllianceInvitation invitation = state.Invitations[i];
            bool expired = world.Frame - invitation.Frame >= frames;
            bool moot = !AllianceRules.IsPlaying(world, invitation.To) || world.Tribes[invitation.From].OutAtFrame >= 0
                || world.Relations.AreAllies(invitation.From, invitation.To);
            if (expired || moot)
            {
                state.RemoveInvitations(x => x == invitation);
                raise(new AllianceInvitationLapsed(invitation.From, invitation.To));
            }
        }
    }

    /// <summary>The units of <paramref name="tribe"/> attacking anything of <paramref name="ally"/> stop.</summary>
    private void StandDown(int tribe, int ally)
    {
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            if (unit.HitPoints <= 0 || unit.Action > ActionNumbers.LastBasicAttack || !Targets(unit.TargetCellCode, ally))
            {
                continue;
            }
            if (CellCode.At(world.Map, new GridPoint(unit.X, unit.Y)) == CellCode.OfUnit(unit.Id))
            {
                actions.Stop(unit);
                unit.TargetCellCode = CellCode.Empty;
                unit.OrderMode = OrderModes.Free;
            }
        }
    }

    private bool Targets(int code, int tribe)
    {
        int unit = CellCode.UnitOf(code), building = CellCode.BuildingOf(code);
        return (unit > 0 && unit <= world.UnitSlots && world.TribeOfUnit(unit) == tribe)
            || (building > 0 && building <= world.BuildingSlots && world.TribeOfBuilding(building) == tribe);
    }
}
