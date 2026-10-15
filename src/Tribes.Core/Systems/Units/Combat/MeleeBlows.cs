using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Combat;

/// <summary>
/// A melee attacker's blow at the cell in front of it: damage from its melee skill and weapon, and
/// a unit hit by a heavy armed blow flinches.
/// </summary>
internal sealed class MeleeBlows
{
    /// <summary>Attack animations from this number on are heavy blows that make the victim flinch.</summary>
    private const int FirstHeavyBlow = 31;

    /// <summary>The weapon kind of bare hands (their blows never make anyone flinch).</summary>
    private const int BareHands = 9;

    /// <summary>The animation of a unit flinching from a blow.</summary>
    private const int FlinchAnimation = 42;

    /// <summary>Animations of a unit caught in a trap (signed); such a unit does not flinch.</summary>
    private const int FirstTrapped = 106, LastTrapped = 108;

    /// <summary>The two hit marks drawn over a flinching unit (as negative building slot values).</summary>
    private const int FirstHitMark = 62, HitMarks = 2;

    private readonly GameWorld world;
    private readonly CellDamage damage;
    private readonly CellClassifier cells;
    private readonly WeaponTables weapons;

    /// <summary>Lands melee blows in <paramref name="world"/>.</summary>
    public MeleeBlows(GameWorld world, CellDamage damage, CellClassifier cells)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.damage = damage ?? throw new ArgumentNullException(nameof(damage));
        this.cells = cells ?? throw new ArgumentNullException(nameof(cells));
        weapons = world.Content.Rules.Weapons;
    }

    /// <summary>
    /// <paramref name="attacker"/> strikes <paramref name="cell"/> for the damage of its melee skill
    /// (skills above the damage table count as its highest) and weapon, aiming at its target;
    /// <paramref name="anchor"/> places the damage number.
    /// </summary>
    public void Strike(Unit attacker, GridPoint cell, int anchor)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        IReadOnlyList<int> bySkill = weapons.MeleeDamageBySkill;
        int hurt = bySkill[Math.Min(attacker.MeleeSkill, bySkill.Count - 1)] + weapons.Weapons[attacker.Weapon].DamageBonus;
        int expected = world.Content.Rules.Actions.Actions[attacker.Action].AttackTargetCategory ?? CellCategories.Empty;
        if (!damage.Strike(Striker.OfUnit(attacker), cell, expected, attacker.TargetIndex, anchor, hurt, 0)
            || attacker.AnimationNumber < FirstHeavyBlow || attacker.Weapon == BareHands)
        {
            return;
        }
        CellProbe probe = cells.ProbeCell(cell, attacker.Tribe);
        if (probe.Category != CellCategories.Unit)
        {
            return;
        }
        Unit victim = world.Unit(probe.Slot);
        if (victim.IsCentred && victim.Animation is < FirstTrapped or > LastTrapped)
        {
            victim.Animation = FlinchAnimation;
            victim.AnimationTick = 0;
            victim.BuildingSlot = -(FirstHitMark + (world.Frame % HitMarks));
        }
    }
}
