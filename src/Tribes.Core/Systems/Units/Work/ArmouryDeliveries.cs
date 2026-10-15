using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// An armoury worker's finished product: a weapon carried to the stock, or a trap carried to the
/// place it was ordered for and set there.
/// </summary>
internal sealed class ArmouryDeliveries
{
    /// <summary>The worker taking a finished weapon to the stock.</summary>
    private const int DeliverProduct = 26;

    /// <summary>A product's stock counter - this + this is the item carried.</summary>
    private const int FirstStockCounter = 5, FirstProductItem = 8;

    /// <summary>The first trap product; trap product p sets the trap kind p + this.</summary>
    private const int TrapKindOffset = 14;

    private readonly GameWorld world;
    private readonly ArmouryTables armoury;

    /// <summary>Handles the products of <paramref name="world"/>'s armouries.</summary>
    public ArmouryDeliveries(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        armoury = world.Content.Rules.Armoury;
    }

    /// <summary><paramref name="worker"/> picks up the product its armoury makes.</summary>
    public void PickUp(Unit worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        if (QueueOf(worker) is not { } queue)
        {
            return;
        }
        worker.CarriedItem = (int)armoury.Products[queue.Current].StockCounter - FirstStockCounter + FirstProductItem;
        worker.CarriedAmount = 1;
    }

    /// <summary>
    /// <paramref name="worker"/> hands over its product: a weapon goes to the stock, a trap is set at
    /// the worker's destination (its order is done; the computer counts its traps). The armoury has
    /// one item less to make.
    /// </summary>
    public void Deliver(Unit worker)
    {
        ArgumentNullException.ThrowIfNull(worker);
        if (QueueOf(worker) is not { } queue)
        {
            return;
        }
        int product = queue.Current;
        if (worker.Action == DeliverProduct)
        {
            world.Tribes[worker.Tribe][armoury.Products[product].StockCounter]++;
        }
        else
        {
            SetTrap(worker, queue.Armoury, product + TrapKindOffset);
        }
        if (queue.Pending(product) > 0)
        {
            queue.SetPending(product, queue.Pending(product) - 1);
        }
    }

    private void SetTrap(Unit worker, int building, int kind)
    {
        var cell = new GridPoint(worker.DestinationX, worker.DestinationY);
        world.Map.SetCell(cell, CellCode.FromCode(CellCode.Trap(worker.Tribe, kind), world));
        foreach (TrapOrder trap in world.Traps)
        {
            if (trap.OwnerBuilding == building && trap.X == cell.X && trap.Y == cell.Y)
            {
                trap.OwnerBuilding = 0;
                break;
            }
        }
        if (TribeRoles.IsComputer(world, worker.Tribe))
        {
            TribeRoles.Ai(world, worker.Tribe).Flags++;
        }
    }

    /// <summary>The production queue of the armoury <paramref name="worker"/> runs, or null.</summary>
    private ArmouryOrders? QueueOf(Unit worker)
    {
        foreach (ArmouryOrders queue in world.Tribes[worker.Tribe].Armouries)
        {
            if (queue.Armoury > 0 && world.Building(queue.Armoury).OperatorOrBeds == worker.Id)
            {
                return queue;
            }
        }
        return null;
    }
}
