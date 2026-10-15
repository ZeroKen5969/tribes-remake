using System;
using Tribes.Core.Commands;

namespace Tribes.Core.Simulation;

/// <summary>The players' orders (see <see cref="OrderProcessor"/>).</summary>
public sealed partial class RuleSystems
{
    private OrderProcessor? playerOrders;

    /// <summary>Checks and carries out the players' orders on this world.</summary>
    public OrderProcessor PlayerOrders => playerOrders ??= new OrderProcessor(this);

    /// <summary>Checks and carries out <paramref name="order"/>; see <see cref="OrderProcessor.Apply(GameOrder)"/>.</summary>
    public OrderOutcome ApplyOrder(GameOrder order) => PlayerOrders.Apply(order);

    /// <summary>
    /// Decodes and carries out a payload the player of <paramref name="tribe"/> sent; see
    /// <see cref="OrderProcessor.Apply(int, ReadOnlySpan{byte})"/>.
    /// </summary>
    public OrderOutcome ApplyOrder(int tribe, ReadOnlySpan<byte> payload) => PlayerOrders.Apply(tribe, payload);
}
