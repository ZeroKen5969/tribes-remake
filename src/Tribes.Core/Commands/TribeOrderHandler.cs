using System;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Commands;

/// <summary>Orders for the whole tribe: calls to arms, the hint option, the tutorial's help step and story scenes.</summary>
internal sealed class TribeOrderHandler
{
    /// <summary>The status bits of a unit called to arms.</summary>
    private const UnitStatus CalledToArms = UnitStatus.RallyPending | UnitStatus.Mustered;

    private readonly RuleSystems systems;
    private readonly GameWorld world;
    private readonly OrderChecks checks;

    public TribeOrderHandler(RuleSystems systems, OrderChecks checks)
    {
        this.systems = systems ?? throw new ArgumentNullException(nameof(systems));
        this.checks = checks ?? throw new ArgumentNullException(nameof(checks));
        world = systems.World;
    }

    public OrderOutcome Call(CallArmy order)
    {
        if (!checks.HasPlayerState(order.Tribe))
        {
            return OrderOutcome.NotAvailable;
        }
        ArmyOrders army = TribeRoles.Army(world, order.Tribe);
        bool offered = order.Call switch
        {
            ArmyCall.Rally => !army.AllOut && !army.Rally,
            ArmyCall.AllOut => !army.AllOut,
            _ => false,
        };
        if (!offered)
        {
            return OrderOutcome.NotOffered;
        }
        if (!checks.InPlay(order.Point))
        {
            return OrderOutcome.OutOfMap;
        }
        if (order.Call == ArmyCall.AllOut)
        {
            army.AllOut = true;
            army.Rally = false;
        }
        else
        {
            army.Rally = true;
        }
        systems.FrameStart.MusterArmy(order.Tribe);
        army.PointX = order.Point.X;
        army.PointY = order.Point.Y;
        return OrderOutcome.Applied;
    }

    public OrderOutcome Cancel(CancelArmyCall order)
    {
        if (!checks.HasPlayerState(order.Tribe))
        {
            return OrderOutcome.NotAvailable;
        }
        ArmyOrders army = TribeRoles.Army(world, order.Tribe);
        bool offered = order.Call switch
        {
            ArmyCall.Rally => !army.AllOut && army.Rally,
            ArmyCall.AllOut => army.AllOut,
            _ => false,
        };
        if (!offered)
        {
            return OrderOutcome.NotOffered;
        }
        if (order.Call == ArmyCall.AllOut)
        {
            army.AllOut = false;
        }
        else
        {
            army.Rally = false;
        }
        Disband(order.Tribe);
        return OrderOutcome.Applied;
    }

    public OrderOutcome SetHints(SetHints order)
    {
        if (!checks.HasPlayerState(order.Tribe))
        {
            return OrderOutcome.NotAvailable;
        }
        TribeRoles.Hints(world, order.Tribe).Enabled = order.Enabled;
        return OrderOutcome.Applied;
    }

    public OrderOutcome ViewedHelp(ViewedHelp order)
    {
        if (!checks.HasPlayerState(order.Tribe))
        {
            return OrderOutcome.NotAvailable;
        }
        systems.OpenedGameMenu();
        return OrderOutcome.Applied;
    }

    public OrderOutcome AdvanceScene(AdvanceScene order)
    {
        if (!checks.HasPlayerState(order.Tribe) || !systems.CanContinueScene)
        {
            return OrderOutcome.NotAvailable;
        }
        systems.ContinueScene();
        return OrderOutcome.Applied;
    }

    /// <summary>
    /// The army is sent home: every slot of the tribe leaves it, and idle units that were walking
    /// on their own stop at the next cell.
    /// </summary>
    private void Disband(int tribe)
    {
        ListView<Unit> units = world.UnitsOf(tribe);
        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            unit.Status &= ~CalledToArms;
            if (unit.HitPoints > 0 && unit.Action == ActionNumbers.Idle && unit.OrderMode == OrderModes.Free)
            {
                systems.ThreatWatch.StopAtNextCell(unit, settle: false);
            }
        }
    }
}
