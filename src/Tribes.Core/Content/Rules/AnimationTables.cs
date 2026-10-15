using System.Collections.Generic;

namespace Tribes.Core.Content.Rules;

/// <summary>
/// An animation as the rules see it (<see cref="World.Unit.AnimationNumber"/>, an animal's action):
/// how long it runs and on which frame it acts.
/// </summary>
/// <param name="Id">Animation number 0..161.</param>
/// <param name="FrameCount">Frames it lasts; a death or one-shot animation has ended when the frame counter reaches it.</param>
/// <param name="TriggerFrame">The frame on which it acts on the cell in front of the actor; -1 never.</param>
/// <param name="TriggerParameter">What the action on that frame does (passed to the cell effect).</param>
/// <param name="LoopMode">
/// -2 plays once and ends the action step; -1 runs until the action's
/// <see cref="ActionDefinition.ActionTicks"/>; a value of 0 or more also runs on the action ticks,
/// and an animation picked with a roll of 85 or more is replaced by that animation.
/// </param>
/// <param name="EnergyDecayPeriod">
/// For animations 0..14 (walking and standing): replaces the action's
/// <see cref="ActionDefinition.EnergyDecayPeriod"/>; 127 never; null for the others.
/// </param>
/// <param name="DirectionGroup">
/// Which row of <see cref="AnimationTables.SpriteDirectionGroups"/> turns a facing into the
/// animation's picture direction (presentation).
/// </param>
public sealed record AnimationDefinition(int Id, int FrameCount, int TriggerFrame, int TriggerParameter,
    int LoopMode, int? EnergyDecayPeriod, int DirectionGroup);

/// <summary>Animation timing.</summary>
/// <param name="Animations">Animations by number.</param>
/// <param name="SpriteDirectionGroups">
/// Four rows of eight: the picture direction of facings 0..7 for animations drawn with 1, 2, 4 or 8 directions.
/// </param>
public sealed record AnimationTables(IReadOnlyList<AnimationDefinition> Animations,
    IReadOnlyList<IReadOnlyList<int>> SpriteDirectionGroups);
