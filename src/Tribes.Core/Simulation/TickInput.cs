using System;
using System.Collections.Generic;
using Tribes.Core.Commands;

namespace Tribes.Core.Simulation;

/// <summary>
/// One player order of a tick: either an order made on this machine, or the payload a player
/// sent (decoded with <see cref="GameOrderCodec"/> for the tribe of the slot that sent it).
/// </summary>
public sealed class PlayerOrder
{
    private PlayerOrder(int tribe, GameOrder? order, ReadOnlyMemory<byte> payload)
    {
        Tribe = tribe;
        Order = order;
        Payload = payload;
    }

    /// <summary>The tribe the order is for.</summary>
    public int Tribe { get; }

    /// <summary>The order, when it was given as one; null for a payload.</summary>
    public GameOrder? Order { get; }

    /// <summary>The payload, when the order was given as one; empty otherwise.</summary>
    public ReadOnlyMemory<byte> Payload { get; }

    /// <summary>An order made on this machine (its tribe is <see cref="GameOrder.Tribe"/>).</summary>
    public static PlayerOrder From(GameOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new PlayerOrder(order.Tribe, order, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>A payload the player of <paramref name="tribe"/> sent; one that is not an order is turned down when applied.</summary>
    public static PlayerOrder FromPayload(int tribe, ReadOnlyMemory<byte> payload) => new(tribe, null, payload);

    /// <summary>Checks the order and carries it out on <paramref name="systems"/>.</summary>
    internal OrderOutcome ApplyTo(RuleSystems systems) =>
        Order is not null ? systems.ApplyOrder(Order) : systems.ApplyOrder(Tribe, Payload.Span);
}

/// <summary>What a tick is given: the players' orders and what the host's screen does.</summary>
public sealed record TickInput
{
    /// <summary>A tick without orders that runs normally.</summary>
    public static TickInput Idle { get; } = new();

    /// <summary>The orders, carried out first, in this order (the same on every peer).</summary>
    public IReadOnlyList<PlayerOrder> Orders { get; init; } = Array.Empty<PlayerOrder>();

    /// <summary>
    /// The host holds the game (a menu, a hint or the help is open, a scene step is still sliding
    /// in): the orders are carried out, but neither the world nor a scene moves on.
    /// </summary>
    public bool Paused { get; init; }

    /// <summary>The player's unit roster is open (a tutorial step waits for that).</summary>
    public bool RosterOpen { get; init; }

    /// <summary>A normal tick with <paramref name="orders"/>.</summary>
    public static TickInput WithOrders(params GameOrder[] orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return new TickInput { Orders = Array.ConvertAll(orders, PlayerOrder.From) };
    }
}
