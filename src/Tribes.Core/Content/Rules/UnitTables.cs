using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Core.Content.Rules;

/// <summary>
/// When a unit shows a thought icon (<see cref="World.Unit.Thought"/>): 0 hungry, 1 couple, 2 tired,
/// 3 hurt, 4..8 five bands of happiness, 9 homeless, 10 no weapon (unconfirmed).
/// </summary>
/// <param name="Thought">The icon number.</param>
/// <param name="Property">The unit property checked.</param>
/// <param name="Min">Smallest value (inclusive) that shows the icon.</param>
/// <param name="Max">Largest value (inclusive) that shows the icon.</param>
/// <param name="ChancePercent">Chance of showing it at each check.</param>
public sealed record ThoughtRule(int Thought, EntityProperty Property, int Min, int Max, int ChancePercent);

/// <summary>How maximum hit points change with age.</summary>
/// <param name="Age">The age in years 0..100.</param>
/// <param name="MaxHitPointsDelta">
/// Change of maximum hit points on reaching the age (plus a random -2..2; one less for gender 1).
/// </param>
/// <param name="MaxHitPointsCap">The highest maximum hit points at the age.</param>
public sealed record AgeStep(int Age, int MaxHitPointsDelta, int MaxHitPointsCap);

/// <summary>The portraits a unit picks from (<see cref="World.Unit.Portrait"/> = First + random(Count)).</summary>
/// <param name="Gender">The unit's gender.</param>
/// <param name="AgeClass">0 child (below 12), 1 aged 12..34, 2 aged 35 or more, 3 enemy unit, 4 the enemy leader (slot 51).</param>
/// <param name="First">First portrait.</param>
/// <param name="Count">Number of portraits.</param>
public sealed record PortraitRange(int Gender, int AgeClass, int First, int Count);

/// <summary>A unit property a council scene sets on the seated units, saved and put back afterwards.</summary>
/// <param name="Property">The property.</param>
/// <param name="Value">
/// The value set; <see cref="EntityProperty.UnitBuildingSlot"/> takes the seat + 1 and
/// <see cref="EntityProperty.UnitAnimation"/> the seat's animation instead.
/// </param>
public sealed record MeetingOverride(EntityProperty Property, int Value);

/// <summary>Unit needs, aging, portraits and council scenes.</summary>
/// <param name="Thoughts">Thought rules by icon.</param>
/// <param name="AgeSteps">Aging steps by age.</param>
/// <param name="Portraits">Portrait ranges by gender * 5 + age class.</param>
/// <param name="MeetingOverrides">Properties a council scene sets, in order.</param>
/// <param name="MeetingSeatAnimations">The animation of each council seat (0 is the chief's).</param>
public sealed record UnitTables(IReadOnlyList<ThoughtRule> Thoughts, IReadOnlyList<AgeStep> AgeSteps,
    IReadOnlyList<PortraitRange> Portraits, IReadOnlyList<MeetingOverride> MeetingOverrides,
    IReadOnlyList<int> MeetingSeatAnimations);
