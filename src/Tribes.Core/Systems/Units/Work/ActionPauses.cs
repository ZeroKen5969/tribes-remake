using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// A unit pausing its action: it remembers the action, rests a while (its sub-state holds a
/// variant rolled from the action's pause variant) and takes the action up again afterwards.
/// </summary>
internal sealed class ActionPauses
{
    /// <summary>The pausing action; the remembered action is resumed after it.</summary>
    public const int Pausing = 83;

    /// <summary>Mask of the variant roll.</summary>
    private const int RollMask = 0xFFFFFF;

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly IReadOnlyList<ActionDefinition> definitions;
    private readonly IReadOnlyList<PauseVariantRange> variants;

    /// <summary>Lets the units of <paramref name="world"/> pause.</summary>
    public ActionPauses(GameWorld world, UnitActions actions)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        definitions = world.Content.Rules.Actions.Actions;
        variants = world.Content.Rules.Actions.PauseVariants;
    }

    /// <summary>
    /// <paramref name="unit"/> pauses its action. An action without a pause variant pauses like the
    /// last variant (the original reads that one's bounds from outside its table, which holds the same).
    /// </summary>
    public void Pause(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        unit.SavedAction = unit.Action;
        actions.SetAction(unit, Pausing);
        int variant = definitions[unit.SavedAction].PauseVariant;
        PauseVariantRange range = variants[variant >= 0 ? variant : variants.Count - 1];
        unit.ActionSubState = range.MinSubState + world.Random.NextMasked(RollMask, range.MaxSubState - range.MinSubState + 1);
    }
}
