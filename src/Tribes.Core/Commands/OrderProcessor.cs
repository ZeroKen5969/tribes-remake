using System;
using Tribes.Core.Simulation;

namespace Tribes.Core.Commands;

/// <summary>
/// Checks and carries out the players' orders on a world. Run every order of a tick before the
/// tick's world update, in the order the session gives them (the same on every peer). An order
/// that does not fit the world (unknown units or buildings, another tribe's, not offered now, off
/// the map) is turned down without changing anything and without throwing, so orders from remote
/// players can be applied as they come. A tribe whose player is away or gave up takes no orders.
/// </summary>
public sealed class OrderProcessor
{
    private readonly OrderChecks checks;
    private readonly UnitOrderHandler units;
    private readonly BuildingOrderHandler buildings;
    private readonly TribeOrderHandler tribes;
    private readonly AllianceOrderHandler alliances;

    /// <summary>Applies orders through <paramref name="systems"/> to its world.</summary>
    public OrderProcessor(RuleSystems systems)
    {
        ArgumentNullException.ThrowIfNull(systems);
        checks = new OrderChecks(systems.World);
        units = new UnitOrderHandler(systems, checks);
        buildings = new BuildingOrderHandler(systems, checks);
        tribes = new TribeOrderHandler(systems, checks);
        alliances = new AllianceOrderHandler(systems);
    }

    /// <summary>Checks <paramref name="order"/> and carries it out; returns what became of it.</summary>
    public OrderOutcome Apply(GameOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (!checks.IsTribe(order.Tribe))
        {
            return OrderOutcome.UnknownTribe;
        }
        if (!checks.MayGiveOrders(order.Tribe))
        {
            return OrderOutcome.NotAvailable;
        }
        return order switch
        {
            MoveUnits o => units.Move(o),
            FetchWeapon o => units.Fetch(o),
            DeliverToStore o => units.Deliver(o),
            TendNeed o => units.TendNeed(o),
            CallOutUnit o => units.CallOut(o),
            AppointChief o => units.AppointChief(o),
            PlaceBuilding o => buildings.Place(o),
            PlaceTrap o => buildings.PlaceTrap(o),
            DemolishBuilding o => buildings.Demolish(o),
            OrderBuildingWork o => buildings.Work(o),
            EvictUnit o => buildings.Evict(o),
            SetArmouryOrder o => buildings.SetArmouryOrder(o),
            CastSpell o => buildings.CastSpell(o),
            CastSpellAt o => buildings.CastSpellAt(o),
            CallArmy o => tribes.Call(o),
            CancelArmyCall o => tribes.Cancel(o),
            SetHints o => tribes.SetHints(o),
            ViewedHelp o => tribes.ViewedHelp(o),
            AdvanceScene o => tribes.AdvanceScene(o),
            InviteAlly o => alliances.Invite(o),
            AnswerAlliance o => alliances.Answer(o),
            LeaveAlliance o => alliances.Leave(o),
            TransferGoods o => alliances.Transfer(o),
            _ => OrderOutcome.NotAvailable,
        };
    }

    /// <summary>
    /// Decodes a payload the player of <paramref name="tribe"/> sent (<see cref="GameOrderCodec"/>)
    /// and carries it out; a payload that is not an order is ignored (<see cref="OrderOutcome.Malformed"/>).
    /// </summary>
    public OrderOutcome Apply(int tribe, ReadOnlySpan<byte> payload) =>
        GameOrderCodec.TryDecode(payload, tribe, out GameOrder? order) ? Apply(order) : OrderOutcome.Malformed;
}
