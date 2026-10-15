using Tribes.Core.Commands;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>The orders of the tribes panel (the rules check each one and refuse what does not fit).</summary>
public static class AllianceOrders
{
    /// <summary><paramref name="tribe"/> invites <paramref name="other"/> into an alliance.</summary>
    public static GameOrder Invite(int tribe, int other) => new InviteAlly(tribe, other);

    /// <summary><paramref name="tribe"/> answers the invitation of <paramref name="inviter"/>.</summary>
    public static GameOrder Answer(int tribe, int inviter, bool accept) => new AnswerAlliance(tribe, inviter, accept);

    /// <summary><paramref name="tribe"/> leaves its alliance with <paramref name="ally"/>.</summary>
    public static GameOrder Leave(int tribe, int ally) => new LeaveAlliance(tribe, ally);

    /// <summary><paramref name="tribe"/> gives <paramref name="ally"/> <paramref name="amount"/> of <paramref name="good"/>.</summary>
    public static GameOrder Gift(int tribe, int ally, TribeCounter good, int amount) => new TransferGoods(tribe, ally, good, amount);
}
