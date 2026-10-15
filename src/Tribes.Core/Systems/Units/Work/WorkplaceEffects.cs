using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// What working in a building does to the units there: the building trains them, an entertainer
/// cheers up and rests the others, a witch doctor heals them.
/// </summary>
internal sealed class WorkplaceEffects
{
    /// <summary>The highest happiness and energy.</summary>
    private const int Highest = 99, Lowest = 0;

    /// <summary>An entertainer gives (skill + this) / 2 happiness and energy.</summary>
    private const int EntertainBase = 2, EntertainDivisor = 2;

    /// <summary>A witch doctor's treatment heals this much.</summary>
    private const int Treatment = 10;

    private readonly GameWorld world;
    private readonly UnitLifecycle lifecycle;
    private readonly IReadOnlyList<BuildingKind> kinds;

    /// <summary>Applies the effects of work places in <paramref name="world"/>.</summary>
    public WorkplaceEffects(GameWorld world, UnitLifecycle lifecycle)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>
    /// One training check of each training a building of <paramref name="kind"/> gives at level index
    /// <paramref name="levelIndex"/> (0 = level 1); returns whether all of them are over (their stats are at their caps).
    /// </summary>
    public bool Train(Unit unit, int kind, int levelIndex)
    {
        ArgumentNullException.ThrowIfNull(unit);
        bool allOver = true;
        foreach (TrainingEffect training in kinds[kind].Operation!.TrainingEffects[levelIndex])
        {
            if (!lifecycle.Practise(unit, training.Stat, training.ChancePercent, training.Cap, training.Step))
            {
                allOver = false;
            }
        }
        return allOver;
    }

    /// <summary>The entertainer <paramref name="unit"/> cheers up and rests everyone else in the building it works in.</summary>
    public void Entertain(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int boost = (unit.DjingSkill + EntertainBase) / EntertainDivisor;
        foreach (Unit other in Others(unit))
        {
            other.Happiness = GridMath.Clamp(other.Happiness + boost, Lowest, Highest);
            other.Energy = GridMath.Clamp(other.Energy + boost, Lowest, Highest);
        }
    }

    /// <summary>The witch doctor <paramref name="unit"/> heals everyone else in its building; the healed leave.</summary>
    public void Heal(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        foreach (Unit other in Others(unit))
        {
            lifecycle.ChangeHitPoints(other, Treatment);
            if (other.HitPoints >= other.MaxHitPoints)
            {
                other.Action = ActionNumbers.LeaveBuilding;
                other.OrderMode = OrderModes.Forced;
            }
        }
    }

    /// <summary>The other occupants of the building <paramref name="unit"/> works in.</summary>
    private List<Unit> Others(Unit unit)
    {
        var others = new List<Unit>();
        if (unit.TargetIndex < 1 || unit.TargetIndex > world.BuildingSlots)
        {
            return others;
        }
        for (int id = world.Building(unit.TargetIndex).FirstOccupant; id > 0; id = world.Unit(id).NextOccupantOrCellObject)
        {
            if (id != unit.Id)
            {
                others.Add(world.Unit(id));
            }
        }
        return others;
    }
}
