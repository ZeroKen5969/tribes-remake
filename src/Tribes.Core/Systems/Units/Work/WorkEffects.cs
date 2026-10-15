using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>What one work step did to the effect object.</summary>
/// <param name="EffectValue">The effect property's value afterwards.</param>
/// <param name="Direction">+1 when the effect counts up towards its goal, -1 when it counts down.</param>
internal readonly record struct WorkStep(int EffectValue, int Direction)
{
    /// <summary>Whether the effect has reached <paramref name="goal"/> from the side it moves from.</summary>
    public bool Reached(int goal) => EffectValue * Direction >= goal * Direction;
}

/// <summary>
/// One step of an action's work: the unit's rate (a skill plus a bonus) goes to the action's rate
/// target, and the effect object's property moves by the action's delta.
/// </summary>
internal sealed class WorkEffects
{
    /// <summary>The player's units this near to their chief work at 1.5 times the rate (cells).</summary>
    private const int ChiefPresence = 4;

    /// <summary>The chief's presence multiplies the rate by this fraction.</summary>
    private const int ChiefBonusNumerator = 3, ChiefBonusDenominator = 2;

    private readonly GameWorld world;
    private readonly IReadOnlyList<ActionDefinition> actions;

    /// <summary>Applies work steps in <paramref name="world"/>.</summary>
    public WorkEffects(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        actions = world.Content.Rules.Actions.Actions;
    }

    /// <summary>Does one step of <paramref name="unit"/>'s current action and tells how its effect stands.</summary>
    public WorkStep Apply(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ActionDefinition action = actions[unit.Action];
        int rate = (EntityProperties.Get(world, action.RateProperty, unit.Id) + action.RateBonus)
            * Sign(action.Traits, ActionTraits.RateAdds);
        if (IsWorkSkill(action.RateProperty) && TribeRoles.IsPlayer(world, unit.Tribe)
            && EntitySlots.NearChief(world, unit, ChiefPresence))
        {
            rate = rate * ChiefBonusNumerator / ChiefBonusDenominator;
        }
        if (action.RateTarget.Counter is { } counter)
        {
            world.Tribes[unit.Tribe][counter] += rate;
        }
        else
        {
            EntityProperties.Add(world, action.RateTarget.Property!.Value, unit.Id, rate);
        }
        int direction = Sign(action.Traits, ActionTraits.EffectCountsUp);
        int delta = action.EffectDelta.Constant ?? EntityProperties.Get(world, action.EffectDelta.Property!.Value, unit.Id);
        EntitySlots.Add(world, action.EffectProperty, unit.EffectRecipient, delta * direction);
        return new WorkStep(EntitySlots.Read(world, action.EffectProperty, unit.EffectRecipient), direction);
    }

    /// <summary>Whether <paramref name="property"/> is one of the work skills (gathering .. building).</summary>
    private static bool IsWorkSkill(EntityProperty property) =>
        property >= EntityProperty.UnitHarvestingSkill && property <= EntityProperty.UnitBuildingSkill;

    private static int Sign(ActionTraits traits, ActionTraits positive) => (traits & positive) != 0 ? 1 : -1;
}
