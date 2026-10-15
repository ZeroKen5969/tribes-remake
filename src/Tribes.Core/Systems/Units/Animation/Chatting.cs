using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Animation;

/// <summary>
/// Idle tribe mates chatting: an idle unit turns to a standing mate to its left or right, and a
/// chatting unit's partner in front of it turns back. Each shows the chatting picture of its own
/// direction over it.
/// </summary>
internal sealed class Chatting
{
    /// <summary>Animations of a mate standing around (two standing and four idle ones) who may join a chat.</summary>
    private const int FirstStanding = 8, LastStanding = 9, FirstIdle = 15, LastIdle = 18;

    /// <summary>The weapon kind that is a pile of stones (a unit carrying it chats holding something).</summary>
    private const int StoneWeapon = 1;

    /// <summary>The chat overlay of facing f is <see cref="ChatOverlayBase"/> minus the picture direction of f.</summary>
    private const int ChatOverlayBase = -92;

    /// <summary>The row of picture directions the chat overlay is drawn with.</summary>
    private const int OverlayDirectionGroup = 1;

    private readonly GameWorld world;
    private readonly IReadOnlyList<DirectionStep> directions;
    private readonly IReadOnlyList<int> overlayDirections;
    private readonly GridDistances distances;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public Chatting(GameWorld world, GridDistances distances)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        directions = world.Content.Rules.Movement.Directions;
        overlayDirections = world.Content.Rules.Animations.SpriteDirectionGroups[OverlayDirectionGroup];
    }

    /// <summary>
    /// <paramref name="unit"/> looks for a standing tribe mate on the columns to its left and right
    /// (inner cells of the map only) and starts chatting with the first; returns whether it found one.
    /// </summary>
    public bool StartChat(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        GridRect bounds = world.Map.Bounds;
        for (int x = unit.X - 1; x < unit.X + 2; x += 2)
        {
            for (int y = unit.Y - 1; y < unit.Y + 2; y++)
            {
                if (x <= bounds.Left || x >= bounds.Right - 1 || y <= bounds.Top || y >= bounds.Bottom - 1
                    || MateAt(unit, new GridPoint(x, y)) is not { } mate)
                {
                    continue;
                }
                unit.Animation = ChatAnimation(unit);
                unit.Facing = GridDistances.Facing(distances.Measure(x - unit.X, y - unit.Y));
                mate.Facing = Facings.Opposite(unit.Facing);
                unit.BuildingSlot = ChatOverlayBase - overlayDirections[unit.Facing];
                return true;
            }
        }
        return false;
    }

    /// <summary>A chatting <paramref name="unit"/>'s partner in front of it turns to it and chats back.</summary>
    public void Answer(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        DirectionStep ahead = directions[unit.Facing];
        if (MateAt(unit, new GridPoint(unit.X + ahead.Dx, unit.Y + ahead.Dy)) is not { } mate)
        {
            return;
        }
        mate.Animation = ChatAnimation(mate);
        mate.Facing = Facings.Opposite(unit.Facing);
        mate.AnimationTick = mate.Id < unit.Id ? 0 : -1;
        mate.BuildingSlot = ChatOverlayBase - overlayDirections[mate.Facing];
    }

    /// <summary>A tribe mate of <paramref name="unit"/> standing around at <paramref name="cell"/>, if any.</summary>
    private Unit? MateAt(Unit unit, GridPoint cell)
    {
        if (!world.Map.Contains(cell))
        {
            return null;
        }
        MapCell content = world.Map.CellAt(cell);
        if (content.Content != CellContent.Unit || world.TribeOfUnit(content.Index) != unit.Tribe)
        {
            return null;
        }
        Unit mate = world.Unit(content.Index);
        int animation = mate.Animation;
        return animation is >= FirstStanding and <= LastStanding or >= FirstIdle and <= LastIdle ? mate : null;
    }

    private static int ChatAnimation(Unit unit) =>
        unit.CarriedItem == 0 && unit.Weapon != StoneWeapon ? UnitAnimations.Chatting : UnitAnimations.ChattingHolding;
}
