using System.Collections.Generic;

namespace Tribes.Core.Content.Rules;

/// <summary>What the armoury menu shows for a weapon (presentation; the real costs are the recipes').</summary>
/// <param name="Cost">Wood, stones and bones shown as its cost.</param>
/// <param name="IsThrown">Whether it is shown as a thrown weapon.</param>
/// <param name="Attack">The attack value shown.</param>
public sealed record WeaponDisplay(IReadOnlyList<int> Cost, bool IsThrown, int Attack);

/// <summary>
/// A weapon kind (<see cref="World.Unit.Weapon"/>): 0 club, 1 stone, 2 spear, 3 stone axe, 4 bone
/// mace, 5 tomahawk, 6..8 unused, 9 bare hands.
/// </summary>
/// <param name="Id">Weapon number 0..9.</param>
/// <param name="RangedActionOffset">
/// Added to a basic attack action (0..4) to get the ranged version (5..9); non-zero for thrown weapons.
/// </param>
/// <param name="ProjectileKind">
/// The projectile thrown (index into <see cref="WeaponTables.Projectiles"/>); -1 a melee weapon, -2 none.
/// </param>
/// <param name="DamageBonus">Added to the attacker's damage.</param>
/// <param name="AttackAnimations">
/// Animations of attacks with the weapon; a melee attacker picks among the first 2 + melee skill.
/// Empty for the unused kinds.
/// </param>
/// <param name="RefundAmount">
/// For weapons 0..5: how much goes back to the stock when a unit changes to another weapon; null for the others.
/// </param>
/// <param name="Display">For weapons 0..5: what the armoury menu shows; null for the others.</param>
/// <param name="ProjectileSprite">For weapons 0..5: base picture of the weapon's projectile in flight; -1 none.</param>
/// <param name="ProjectileSpriteFacingStride">
/// For weapons 0..5: picture step per throwing direction (<see cref="World.Unit.ProjectileFacing"/>).
/// </param>
public sealed record WeaponKind(int Id, int RangedActionOffset, int ProjectileKind, int DamageBonus,
    IReadOnlyList<int> AttackAnimations, int? RefundAmount, WeaponDisplay? Display, int? ProjectileSprite,
    int? ProjectileSpriteFacingStride);

/// <summary>A kind of thrown projectile (stone, spear, tomahawk).</summary>
/// <param name="Id">Projectile kind 0..2.</param>
/// <param name="Speed">
/// Flight steps = pixel distance / Speed (at least 1); also the arc's linear term.
/// </param>
/// <param name="ArcDivisorByFacing">
/// Divisor of the flight arc's height by the thrower's facing (10000: flies flat).
/// </param>
/// <param name="RangeTrainingAnimationOffset">
/// For range training (action 72) with a weapon throwing this projectile: the first animation choice used.
/// </param>
/// <param name="FlightFrames">Picture offsets of the projectile along its flight (presentation).</param>
public sealed record ProjectileKind(int Id, int Speed, IReadOnlyList<int> ArcDivisorByFacing,
    int RangeTrainingAnimationOffset, IReadOnlyList<int> FlightFrames);

/// <summary>Weapons, projectiles and melee damage.</summary>
/// <param name="Weapons">Weapon kinds by number.</param>
/// <param name="Projectiles">Projectile kinds by number.</param>
/// <param name="MeleeDamageBySkill">
/// Base melee damage by melee skill 0..9; the weapon's <see cref="WeaponKind.DamageBonus"/> is added.
/// </param>
public sealed record WeaponTables(IReadOnlyList<WeaponKind> Weapons, IReadOnlyList<ProjectileKind> Projectiles,
    IReadOnlyList<int> MeleeDamageBySkill);
