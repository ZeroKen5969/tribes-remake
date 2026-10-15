using System;
using System.Collections.Generic;
using Tribes.Client.World.Scene;
using Tribes.Core.World;
using Tribes.Graphics.Composition;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>A rectangle in map pixels.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>Whether map point (<paramref name="px"/>, <paramref name="py"/>) lies inside.</summary>
    public bool Contains(float px, float py) => px >= X && px < X + Width && py >= Y && py < Y + Height;

    /// <summary>The smallest rectangle holding this one and <paramref name="other"/>.</summary>
    public PixelRect Union(PixelRect other)
    {
        int left = Math.Min(X, other.X), top = Math.Min(Y, other.Y);
        int right = Math.Max(X + Width, other.X + other.Width), bottom = Math.Max(Y + Height, other.Y + other.Height);
        return new PixelRect(left, top, right - left, bottom - top);
    }
}

/// <summary>
/// Where each building is drawn: its footprint and the part of its picture above and beside it
/// (roofs, the tower's platform with its guard), as the object layer composes it. A tap anywhere on
/// that picture is a tap on the building. Pictures are worked out once per kind and level.
/// </summary>
public sealed class BuildingShapes
{
    /// <summary>Building slot the pictures are composed for (the player's; the others only differ in their flag).</summary>
    private const int OwnSlot = 1;

    private readonly ObjectComposer composer;
    private readonly Dictionary<(int Kind, int Level), PixelRect> pictures = new();

    /// <summary>Shapes from the pictures <paramref name="composer"/> draws.</summary>
    public BuildingShapes(ObjectComposer composer)
    {
        this.composer = composer ?? throw new ArgumentNullException(nameof(composer));
    }

    /// <summary>Rows of cells above its footprint a building's picture may reach; a picture never reaches further.</summary>
    public const int RowsAbove = 4;

    /// <summary>Cells beside its footprint a building's picture may reach on either side.</summary>
    public const int CellsBeside = 1;

    /// <summary>
    /// Where building <paramref name="building"/> is drawn, in map pixels: its footprint and its
    /// picture (cell (x, y) starts at (cellWidth * x, cellHeight * y)).
    /// </summary>
    public PixelRect Of(GameWorld world, Building building, int cellWidth, int cellHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(building);
        PixelRect local = Picture(world, building.Kind, building.Level, cellWidth, cellHeight);
        return local with { X = local.X + (building.X * cellWidth), Y = local.Y + (building.Y * cellHeight) };
    }

    /// <summary>The picture of <paramref name="kind"/> at <paramref name="level"/> with its footprint, from its top-left cell.</summary>
    public PixelRect Picture(GameWorld world, int kind, int level, int cellWidth, int cellHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (pictures.TryGetValue((kind, level), out PixelRect known))
        {
            return known;
        }
        (int width, int height) = FootprintCheck.Size(world, Math.Clamp(kind, 0, world.Content.Rules.Buildings.Kinds.Count - 1));
        var footprint = new PixelRect(0, 0, width * cellWidth, height * cellHeight);
        PixelRect picture;
        if (AllianceHall.Is(kind))
        {
            // Its own picture sheet: the footprint and the roof above it.
            int roof = Math.Max(0, AllianceHallArt.StandardHeight - (height * cellHeight));
            picture = footprint.Union(new PixelRect(0, -roof, footprint.Width, roof));
        }
        else
        {
            picture = footprint.Union(Bounds(composer.Compose(ObjectRequest.Building(kind, level, OwnSlot, ObjectPass.Upper)).Drawing.Parts, footprint));
        }
        pictures[(kind, level)] = picture;
        return picture;
    }

    private static PixelRect Bounds(IReadOnlyList<DrawPart> parts, PixelRect empty)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (DrawPart part in parts)
        {
            if (part.Kind != DrawPartKind.Image)
            {
                continue;
            }
            left = Math.Min(left, part.X);
            top = Math.Min(top, part.Y);
            right = Math.Max(right, part.X + part.Width);
            bottom = Math.Max(bottom, part.Y + part.Height);
        }
        return left == int.MaxValue ? empty : new PixelRect(left, top, right - left, bottom - top);
    }
}
