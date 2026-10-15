using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>
/// The computer's orders: weapons (and later traps) from its idle armouries, and disease spells
/// from its sanctuaries cast at the player's side of the map (in a match, around the enemy the
/// computer goes for).
/// </summary>
internal sealed class ComputerOrders
{
    /// <summary>Order intervals are given in units of this many frames; orders come one unit into each interval.</summary>
    private const int IntervalUnit = 50;

    /// <summary>The armoury products an idle armoury has nothing pending of (weapons and traps).</summary>
    private const int OrderedProducts = 9;

    /// <summary>Weapon priorities each order slot looks at: the first all, the second from this position on.</summary>
    private const int SecondOrderFirstPriority = 3;

    /// <summary>Weapon order slots.</summary>
    private const int WeaponOrders = 2;

    /// <summary>The low bits of the computer's flags count the traps it has set.</summary>
    private const int TrapCountMask = 63;

    /// <summary>The ramp-up fraction after which traps are ordered, and the one after which spells are cast.</summary>
    private const int TrapRampDivisor = 3, SpellRampDivisor = 2;

    /// <summary>The task order kind of a spell cast at a place.</summary>
    public const int SpellOrder = 3;

    /// <summary>Spiral rounds searched for a free cell around a picked place (the whole map).</summary>
    private const int WholeMapRounds = 127;

    /// <summary>Random masks of the draws picking an area and a cell in it.</summary>
    private const int AreaRoll = 0xFF, CellRoll = 0xFFFF;

    private readonly GameWorld world;
    private readonly ComputerPlans plans;
    private readonly ComputerTargets targets;
    private readonly SpiralSearch spiral;

    /// <summary>Gives the computer tribes' orders in <paramref name="world"/>.</summary>
    public ComputerOrders(GameWorld world, ComputerPlans plans, ComputerTargets targets, SpiralSearch spiral)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.plans = plans ?? throw new ArgumentNullException(nameof(plans));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
    }

    /// <summary>Whether this frame is the one of each interval of <paramref name="units"/> interval units on which the orders of <paramref name="tribe"/> come.</summary>
    public bool IsOrderFrame(int tribe, int units) => Endless.EndlessPacing.Clock(world, tribe) % (units * IntervalUnit) == IntervalUnit;

    /// <summary>
    /// Every idle armoury of <paramref name="tribe"/> (nothing pending) gets weapon orders — the
    /// first weapon in stock of the priority list, and the first thrown one — and, after a third
    /// of the ramp-up while the computer has set fewer traps than its limit, trap orders, each trap
    /// set at once in one of the mission's trap areas.
    /// </summary>
    public void OrderFromArmouries(int tribe, int rampUp)
    {
        MissionAi ai = plans.For(tribe);
        AiState state = TribeRoles.Ai(world, tribe);
        foreach (ArmouryOrders queue in world.Tribes[tribe].Armouries)
        {
            int armoury = queue.Armoury;
            if (armoury <= 0 || HasPending(queue))
            {
                continue;
            }
            OrderWeapons(tribe, queue, ai.ArmouryOrder);
            if (Endless.EndlessPacing.ComputerFrames(world, tribe) > rampUp / TrapRampDivisor && (state.Flags & TrapCountMask) < ai.TrapOrderChance)
            {
                OrderTraps(tribe, queue, armoury, ai);
            }
        }
    }

    /// <summary>After half of the ramp-up, each sanctuary of <paramref name="tribe"/> queues its spells at places in the spell areas.</summary>
    public void CastSpells(int tribe, int rampUp)
    {
        if (Endless.EndlessPacing.ComputerFrames(world, tribe) <= rampUp / SpellRampDivisor)
        {
            return;
        }
        MissionAi ai = plans.For(tribe);
        IReadOnlyList<CellRect> areas = EnemyAreas(ai.SpellTargetAreas, tribe);
        ListView<Building> buildings = world.BuildingsOf(tribe);
        for (int b = 0; b < buildings.Count; b++)
        {
            Building building = buildings[b];
            if (building.HitPoints <= 0 || building.Kind != BuildingKindNumbers.Sanctuary)
            {
                continue;
            }
            for (int i = 0; i < ai.SpellsPerSanctuary; i++)
            {
                GridPoint cell = PickCell(areas);
                TaskOrder order = world.Tribes[tribe].FreeTaskOrder(world.GrowsOnDemand);
                (order.Building, order.A, order.B, order.Kind) = (building.Id, cell.X, cell.Y, SpellOrder);
            }
        }
    }

    /// <summary>
    /// A random cell of a random area of <paramref name="areas"/>, moved to the nearest free cell
    /// around it (never the cell itself).
    /// </summary>
    public GridPoint PickCell(IReadOnlyList<CellRect> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        CellRect area = areas[world.Random.NextMasked(AreaRoll, areas.Count)];
        int x = area.Left + world.Random.NextMasked(CellRoll, area.Right - area.Left + 1);
        int y = area.Top + world.Random.NextMasked(CellRoll, area.Bottom - area.Top + 1);
        var centre = new GridPoint(x, y);
        return spiral.FindEmptyCell(centre, WholeMapRounds) ?? centre;
    }

    private static bool HasPending(ArmouryOrders queue)
    {
        for (int product = 0; product < OrderedProducts; product++)
        {
            if (queue.Pending(product) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private void OrderWeapons(int tribe, ArmouryOrders queue, AiArmouryOrder order)
    {
        IReadOnlyList<int> priority = world.Content.Rules.Armoury.AiWeaponOrderPriority;
        TribeState stock = world.Tribes[tribe];
        for (int slot = 0; slot < WeaponOrders; slot++)
        {
            for (int i = slot * SecondOrderFirstPriority; i < priority.Count; i++)
            {
                int product = priority[i];
                if (stock.Counter((int)TribeCounter.Clubs + product) != 0)
                {
                    queue.SetPending(product, slot == 0 ? order.PrimaryWeaponQuantity : order.SecondaryWeaponQuantity);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The mission's areas at the enemy's side: as they are in the campaign; in a match around the
    /// enemy the last wave went for, or else the one the computer would go for (unchanged when no
    /// enemy is left).
    /// </summary>
    private IReadOnlyList<CellRect> EnemyAreas(IReadOnlyList<CellRect> areas, int tribe)
    {
        if (world.Mode == WorldMode.Campaign)
        {
            return areas;
        }
        int target = TribeRoles.Ai(world, tribe).WaveTribe;
        if (target < 0 || !TribeRoles.AreEnemies(world, tribe, target) || Match.TribeStanding.IsOut(world, target))
        {
            target = targets.Choose(tribe);
        }
        return targets.Areas(areas, target);
    }

    private void OrderTraps(int tribe, ArmouryOrders queue, int armoury, MissionAi ai)
    {
        IReadOnlyList<int> quantities = ai.ArmouryOrder.TrapQuantities;
        IReadOnlyList<CellRect> areas = EnemyAreas(ai.TrapAreas, tribe);
        for (int trap = 0; trap < quantities.Count; trap++)
        {
            int product = BuildingPlacement.FirstTrapProduct + trap;
            queue.SetPending(product, quantities[trap]);
            for (int i = 0; i < queue.Pending(product); i++)
            {
                GridPoint cell = PickCell(areas);
                int slot = world.FreeTrapOrderSlot();
                TrapOrder order = world.Traps[slot >= 0 ? slot : world.Traps.Count - 1];
                (order.OwnerBuilding, order.X, order.Y, order.Kind) = (armoury, cell.X, cell.Y, BuildingKindNumbers.FirstTrap + trap);
                world.Map.SetCell(cell, new MapCell(CellContent.PendingTrap, 0, tribe));
            }
        }
    }

}
