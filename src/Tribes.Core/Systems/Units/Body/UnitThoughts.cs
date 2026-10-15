using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Body;

/// <summary>
/// The thought bubble over the player's adults. A bubble shows for one check and then pauses for
/// a few; when the pause is over a unit at work may show a thought drawn from the thought rules,
/// and hunger, exhaustion and an unhappy homeless life always show. Spell casters keep their own
/// icon. The bubbles are only shown, but they draw random numbers, so they belong to the rules.
/// </summary>
internal sealed class UnitThoughts
{
    /// <summary>The pause after a bubble lasts 2 + a roll of 0..3 checks.</summary>
    private const int PauseRollMask = 255, PauseRollRange = 4, ShortestPause = 2;

    /// <summary>The thought roll: below <see cref="Thoughts.RolledIcons"/> a rule is tried, else nothing.</summary>
    private const int ThoughtRollMask = 0xFFFFFF, ThoughtRollRange = 22;

    /// <summary>The chance roll of a thought rule.</summary>
    private const int ChanceRollMask = 0xFFFFFF, Percent = 100;

    /// <summary>A homeless unit below this happiness thinks of a home.</summary>
    private const int HomelessUnhappiness = 10;

    private readonly GameWorld world;
    private readonly IReadOnlyList<ThoughtRule> rules;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitThoughts(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        rules = world.Content.Rules.Units.Thoughts;
    }

    /// <summary>One check of <paramref name="unit"/>'s thought bubble.</summary>
    public void Think(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (ShowsSpellIcon(unit.AnimationNumber))
        {
            return;
        }
        if (unit.Thought >= 0)
        {
            unit.Thought = -ShortestPause - world.Random.NextMasked(PauseRollMask, PauseRollRange);
        }
        else if (unit.Thought < Thoughts.None)
        {
            unit.Thought++;
        }
        if (unit.Thought < Thoughts.None || unit.Action <= ActionNumbers.LastBasicAttack)
        {
            return;
        }
        if (unit.Action >= ActionNumbers.FirstCoupleAction && unit.Action <= ActionNumbers.LastCoupleAction)
        {
            unit.Thought = Thoughts.InLove;
            return;
        }
        RollThought(unit);
        if (unit.Home < 1 && unit.Happiness < HomelessUnhappiness)
        {
            unit.Thought = Thoughts.Homeless;
        }
        if (unit.Satiety == 0)
        {
            unit.Thought = Thoughts.Hungry;
        }
        if (unit.Energy == 0)
        {
            unit.Thought = Thoughts.Exhausted;
        }
    }

    /// <summary>Whether a unit playing <paramref name="animation"/> keeps the icon of its spell.</summary>
    public static bool ShowsSpellIcon(int animation) =>
        animation is UnitAnimations.Casting or UnitAnimations.Brewing or UnitAnimations.Healing;

    private void RollThought(Unit unit)
    {
        int icon = world.Random.NextMasked(ThoughtRollMask, ThoughtRollRange);
        if (icon >= Thoughts.RolledIcons)
        {
            return;
        }
        ThoughtRule rule = rules[icon];
        int value = EntityProperties.Get(world, rule.Property, unit.Id);
        if (value >= rule.Min && value <= rule.Max && world.Random.NextMasked(ChanceRollMask, Percent) < rule.ChancePercent)
        {
            unit.Thought = icon;
        }
    }
}
