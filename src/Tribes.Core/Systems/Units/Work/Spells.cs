using System;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Couples;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// The witch doctor's spells: healing, protection, the love potion and marking an enemy area, and
/// the mark on a sanctuary's task order while its spell is being cast.
/// </summary>
internal sealed class Spells
{
    /// <summary>The spell actions.</summary>
    private const int Healing = 94, Protection = 95, LovePotion = 96, MarkEnemy = 97;

    /// <summary>Hit points a healing spell gives.</summary>
    private const int HealingPoints = 10;

    /// <summary>The happiness the love potion gives.</summary>
    private const int Enchanted = 99;

    private readonly GameWorld world;
    private readonly UnitLifecycle lifecycle;
    private readonly Courtship courtship;

    /// <summary>Casts spells in <paramref name="world"/>.</summary>
    public Spells(GameWorld world, UnitLifecycle lifecycle, Courtship courtship)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.courtship = courtship ?? throw new ArgumentNullException(nameof(courtship));
    }

    /// <summary>
    /// <paramref name="caster"/> has cast its spell: its work starts over, it may get better at
    /// witchcraft, and the spell acts on its target. Returns whether the spell is done (a healing
    /// spell is done when the target is fully healed).
    /// </summary>
    public bool Cast(Unit caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        caster.WorkProgress = 0;
        ActionDefinition action = world.Content.Rules.Actions.Actions[caster.Action];
        lifecycle.Practise(caster, action.TrainedStat, action.TrainingChancePercent, action.TrainingCap,
            world.Content.Rules.Skills.TrainableStats[action.TrainedStat].ActionTrainingStep);
        if (caster.Action == MarkEnemy)
        {
            MarkArea(caster);
            return true;
        }
        if (caster.TargetIndex < 1 || caster.TargetIndex > world.UnitSlots)
        {
            return true;
        }
        Unit target = world.Unit(caster.TargetIndex);
        switch (caster.Action)
        {
            case Healing:
                return lifecycle.ChangeHitPoints(target, HealingPoints);
            case Protection:
                target.ProtectionTicks = 0;
                target.Status |= UnitStatus.Protected;
                break;
            case LovePotion:
                target.Happiness = Enchanted;
                if ((target.PairLink & target.CoupleLinkMask) >> 1 == 0)
                {
                    courtship.SeekPartner(target, true, caster.Id);
                }
                break;
        }
        return true;
    }

    /// <summary>
    /// Marks the task order of the building <paramref name="caster"/> runs: <paramref name="casting"/>
    /// 1 while its spell is being cast, 0 when it is under way to the target.
    /// </summary>
    public void MarkCasting(Unit caster, int casting)
    {
        ArgumentNullException.ThrowIfNull(caster);
        foreach (Building building in world.BuildingsOf(caster.Tribe))
        {
            if (building.OperatorOrBeds != caster.Id)
            {
                continue;
            }
            foreach (TaskOrder order in world.Tribes[caster.Tribe].TaskOrders)
            {
                if (order.Building == building.Id)
                {
                    order.Extra = casting;
                    return;
                }
            }
            return;
        }
    }

    /// <summary>
    /// The caster marks its destination as enemy ground: the tribe's alarm calls move down one place
    /// (the oldest is dropped) and the new one takes the first place, known to the caster's tribe
    /// when a player plays it.
    /// </summary>
    private void MarkArea(Unit caster)
    {
        AlarmCall[] alarms = world.Tribes[caster.Tribe].Alarms;
        for (int i = alarms.Length - 2; i >= 0; i--)
        {
            if (alarms[i].Age >= 0)
            {
                alarms[i + 1].TakeCallOf(alarms[i]);
            }
        }
        alarms[0].X = caster.DestinationX;
        alarms[0].Y = caster.DestinationY;
        alarms[0].Age = 0;
        alarms[0].ForgetSpotted();
        if (TribeRoles.IsPlayer(world, caster.Tribe))
        {
            alarms[0].MarkKnownTo(caster.Tribe);
        }
    }
}
