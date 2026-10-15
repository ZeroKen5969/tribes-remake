using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>One cell of a planned footprint and whether a building may go there.</summary>
/// <param name="Cell">The map cell.</param>
/// <param name="Free">In play, empty and in the tribe's view now.</param>
public readonly record struct FootprintCell(GridPoint Cell, bool Free);

/// <summary>
/// Whether a building or trap can be placed, cell by cell, as the rules decide it for a player's
/// order: every cell of the footprint must be on the playable map, empty, and seen by the tribe
/// right now. The ghost of the build menu shows this before the order is sent.
/// </summary>
public static class FootprintCheck
{
    /// <summary>The footprint of building kind <paramref name="kind"/> with its top-left cell at <paramref name="origin"/>.</summary>
    public static IReadOnlyList<FootprintCell> Cells(GameWorld world, int tribe, int kind, GridPoint origin)
    {
        ArgumentNullException.ThrowIfNull(world);
        BuildingKind definition = world.Content.Rules.Buildings.Kinds[kind];
        FogOfWar fog = world.Fog(tribe);
        var cells = new List<FootprintCell>(definition.Width * definition.Height);
        for (int dy = 0; dy < definition.Height; dy++)
        {
            for (int dx = 0; dx < definition.Width; dx++)
            {
                var cell = new GridPoint(origin.X + dx, origin.Y + dy);
                bool free = world.Map.InPlay(cell) && world.Map.CellAt(cell).IsEmpty && fog.IsVisible(cell);
                cells.Add(new FootprintCell(cell, free));
            }
        }
        return cells;
    }

    /// <summary>Whether every cell of the footprint is free.</summary>
    public static bool IsFree(GameWorld world, int tribe, int kind, GridPoint origin)
    {
        foreach (FootprintCell cell in Cells(world, tribe, kind, origin))
        {
            if (!cell.Free)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The footprint size of building kind <paramref name="kind"/>.</summary>
    public static (int Width, int Height) Size(GameWorld world, int kind)
    {
        ArgumentNullException.ThrowIfNull(world);
        BuildingKind definition = world.Content.Rules.Buildings.Kinds[kind];
        return (definition.Width, definition.Height);
    }

    /// <summary>
    /// The top-left cell that puts the footprint's middle under map point
    /// (<paramref name="px"/>, <paramref name="py"/>): the ghost follows the finger by its centre.
    /// </summary>
    public static GridPoint OriginUnder(GameWorld world, int kind, float px, float py, int cellWidth, int cellHeight)
    {
        (int width, int height) = Size(world, kind);
        int x = (int)Math.Floor((px / cellWidth) - ((width - 1) / 2f));
        int y = (int)Math.Floor((py / cellHeight) - ((height - 1) / 2f));
        return new GridPoint(x, y);
    }
}
