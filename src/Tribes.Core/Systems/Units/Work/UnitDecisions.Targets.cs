using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>Following a target, losing it, and answering the army's call.</summary>
internal sealed partial class UnitDecisions
{
    /// <summary>Bare hands use the other variant of the basic attacks (0 and 1, 3 and 4).</summary>
    private const int BareHands = 9, FirstUnitAttack = 0, FirstBuildingAttack = 3, VariantBit = 1;

    /// <summary>The spells whose target is gone end with the next spell (the area mark does not).</summary>
    private const int FirstSpell = 94, LastSpell = 97, LastTargetedSpell = 96;

    /// <summary>Marks of a sanctuary's task order: the spell is on its way.</summary>
    private const int SpellUnderWay = 0;

    /// <summary>A unit answering the army's call runs (run bonus) and is armed first when it has no weapon (action 50).</summary>
    private const int Hurry = 3, ArmUp = 50;

    /// <summary>The store, where the unarmed army members are armed.</summary>
    private const int Store = 6;

    /// <summary>Units that hunger less than this leave the kitchen for the army's call only when everyone is called.</summary>
    private const int FedEnough = 30;

    /// <summary>
    /// The turn's unit keeps after its target: when the target is gone it reacts to that first;
    /// a unit following a target walks after it while it is within its sight + 2 (planning a route
    /// when out of reach, a caster on its way marks its order), and a tower guard whose target got
    /// out of throwing range goes back to its post.
    /// </summary>
    private void Follow(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        int target = unit.TargetIndex;
        if (EntitySlots.Read(world, definitions[unit.Action].TargetAliveProperty, target) <= 0)
        {
            TargetLost(turn);
        }
        ActionDefinition action = definitions[unit.Action];
        if ((action.Traits & ActionTraits.FollowsTarget) == 0)
        {
            return;
        }
        int cells = distances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Cells;
        GridPoint at = TargetCell(action, target);
        if (unit.Action is ActionNumbers.TowerGuard or ActionNumbers.TowerAnimalGuard)
        {
            if (cells > unit.ThrowRange)
            {
                BackToPost(unit);
                return;
            }
            (unit.DestinationX, unit.DestinationY) = (at.X, at.Y);
            return;
        }
        if (cells <= unit.SightRadius + FollowSlack)
        {
            if (unit.DestinationX != at.X || unit.DestinationY != at.Y)
            {
                (unit.DestinationX, unit.DestinationY) = (at.X, at.Y);
                if (cells > routes.ActionRange(unit))
                {
                    WalkAfter(turn);
                }
            }
            return;
        }
        if (unit.PathLength == 0)
        {
            if (unit.Action <= ActionNumbers.LastBasicAttack && unit.OrderMode == OrderModes.Free)
            {
                actions.Stop(unit);
                (unit.DestinationX, unit.DestinationY) = (unit.X, unit.Y);
                return;
            }
            (unit.DestinationX, unit.DestinationY) = (at.X, at.Y);
        }
    }

    private void WalkAfter(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        services.Targets.Approach(turn);
        if (unit.Action is >= FirstSpell and <= LastSpell && (unit.Status & UnitStatus.SpellPending) != 0)
        {
            services.Spells.MarkCasting(unit, SpellUnderWay);
            unit.Status &= ~UnitStatus.SpellPending;
        }
    }

    /// <summary>
    /// The target of the turn's unit is gone: a hunter on its own goes on hunting, on a one-off order it
    /// stops; a tower guard goes back to its post; a fighter looks for the next threat (a child stops);
    /// a witch doctor takes its next spell.
    /// </summary>
    private void TargetLost(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        if (unit.Action is ActionNumbers.HuntMelee or ActionNumbers.HuntRanged)
        {
            if ((unit.Status & UnitStatus.OneOffHunt) == 0)
            {
                orders.FinishOrder(unit, OrderModes.Keep);
            }
            else
            {
                actions.Stop(unit);
            }
        }
        if (unit.Action is ActionNumbers.TowerShot or ActionNumbers.TowerGuard or ActionNumbers.TowerAnimalGuard)
        {
            BackToPost(unit);
        }
        if ((definitions[unit.Action].Traits & ActionTraits.StopsWhenTargetGone) != 0)
        {
            LooksForAnother(turn);
        }
        if (unit.Action is >= FirstSpell and <= LastTargetedSpell)
        {
            services.Targets.NextSpell(turn);
        }
    }

    private void LooksForAnother(UnitTurn turn)
    {
        Unit unit = turn.Unit;
        if (unit.Age < UnitAges.Adult)
        {
            actions.Stop(unit);
            return;
        }
        if (unit.Weapon == BareHands && unit.Action is FirstUnitAttack or FirstBuildingAttack)
        {
            unit.Action ^= VariantBit;
        }
        turn.MayActFreely = true;
        turn.RegularDecision = false;
        if (unit.Action > ActionNumbers.LastBasicAttack || !services.Threats.Look(turn))
        {
            actions.Stop(unit);
            if (unit.AnimationNumber > AnimationNumbers.LastWalking)
            {
                (unit.DestinationX, unit.DestinationY) = (unit.X, unit.Y);
            }
        }
    }

    /// <summary>A tower guard goes back to its post in the tower it holds, as the idle action of its position.</summary>
    private void BackToPost(Unit unit)
    {
        distances.AimAtHoldingBuilding(unit, false);
        if (unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots
            && positions.Normal(world.Building(unit.TargetIndex).Kind, unit.BuildingSlot) is { } post)
        {
            actions.SetAction(unit, post.IdleAction);
        }
        services.Animation.Start(unit, 0);
        unit.AnimationTick = -1;
    }

    /// <summary>The cell of <paramref name="target"/> as <paramref name="action"/> locates it, or (0, 0) when it has none.</summary>
    private GridPoint TargetCell(ActionDefinition action, int target)
    {
        if (action.TargetPositionProperty is not { } position)
        {
            return default;
        }
        return new GridPoint(EntitySlots.Read(world, position, target), EntitySlots.Read(world, position + 1, target));
    }
}
