using System;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;

namespace Tribes.Core.Commands;

/// <summary>
/// The alliance orders of endless worlds (<see cref="AllianceRules"/>): an order that does not fit
/// (another world, a tribe that may not, no such invitation, goods the tribe does not have) changes
/// nothing and is turned down as not available.
/// </summary>
internal sealed class AllianceOrderHandler
{
    private readonly RuleSystems systems;

    public AllianceOrderHandler(RuleSystems systems) => this.systems = systems ?? throw new ArgumentNullException(nameof(systems));

    public OrderOutcome Invite(InviteAlly order) =>
        Outcome(systems.World.Endless != null && systems.Alliances.Invite(order.Tribe, order.Invitee));

    public OrderOutcome Answer(AnswerAlliance order) =>
        Outcome(systems.World.Endless != null && systems.Alliances.Answer(order.Tribe, order.Inviter, order.Accept));

    public OrderOutcome Leave(LeaveAlliance order) =>
        Outcome(systems.World.Endless != null && systems.Alliances.Leave(order.Tribe, order.Ally));

    public OrderOutcome Transfer(TransferGoods order) =>
        Outcome(systems.World.Endless != null && systems.Alliances.Transfer(order.Tribe, order.Ally, order.Good, order.Amount) >= 0);

    private static OrderOutcome Outcome(bool applied) => applied ? OrderOutcome.Applied : OrderOutcome.NotAvailable;
}
