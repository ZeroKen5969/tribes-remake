using System.Collections.Generic;

namespace Tribes.Core.Content.Rules;

/// <summary>A kind of wild animal (<see cref="World.Animal.Kind"/>).</summary>
/// <param name="Id">Kind number 0..2.</param>
/// <param name="SpeedClass">
/// Its speed class (<see cref="MovementTables.SpeedClasses"/>); it also re-plans every SpeedClass frames.
/// </param>
/// <param name="MaxHitPoints">Hit points at birth.</param>
/// <param name="FleeThreshold">
/// It flees when this is at most the provocation (50 on seeing a unit or an occupied building, 0
/// when hit), otherwise it turns on the provoker: 0 always flees, 100 always attacks.
/// </param>
/// <param name="AttackDamage">Base damage of its attack; the dealt damage is AttackDamage * (2..4) / 4.</param>
/// <param name="CarcassAmount">Food in the carcass (resource node kind 6) it leaves.</param>
/// <param name="IdleAnimations">Its two idle animations, picked half and half.</param>
public sealed record AnimalKind(int Id, int SpeedClass, int MaxHitPoints, int FleeThreshold, int AttackDamage,
    int CarcassAmount, IReadOnlyList<int> IdleAnimations);

/// <summary>The animal kinds.</summary>
/// <param name="Kinds">Animal kinds by number.</param>
public sealed record AnimalTables(IReadOnlyList<AnimalKind> Kinds);
