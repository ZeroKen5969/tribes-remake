using System;
using System.Collections.Generic;
using Godot;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.World.Dev;

/// <summary>
/// <c>--lineup</c> in a game of the dev scene's own: one row per tribe (in its colour) on open
/// ground by tribe 0's camp, each row showing a newborn sitting, a child, a man, a woman, a man
/// carrying wood, a man with a club and an adult lying, so every tribe colour can be checked on
/// every kind of villager in one picture. <c>--look=T</c> puts the camera over tribe T's camp.
/// </summary>
public partial class WorldTest
{
    private const int NewbornPose = 71, LyingPose = 45, Standing = 0, CarriedWood = 1, Club = 0, LineupFacing = 2;
    private const int CellGap = 2;

    private GridPoint? lineupCentre;

    /// <summary>Age, sex, pose, load and weapon of each villager of a row.</summary>
    private static readonly (int Age, int Gender, int Pose, int Carried, int Weapon)[] LineupLooks =
    {
        (2, 0, NewbornPose, 0, -1), (6, 1, Standing, 0, -1), (30, 0, Standing, 0, -1), (30, 1, Standing, 0, -1),
        (30, 0, Standing, CarriedWood, -1), (30, 0, Standing, 0, Club), (30, 1, LyingPose, 0, -1),
    };

    private void LineUp(GameWorld world, Dictionary<string, string> args)
    {
        if (!args.ContainsKey("lineup"))
        {
            return;
        }
        int columns = LineupLooks.Length * CellGap, rows = world.Tribes.Count * CellGap;
        if (OpenGround(world, world.Tribes[0].Camp, columns, rows) is not GridPoint origin)
        {
            GD.PrintErr($"WORLD_TEST lineup: no open ground of {columns}x{rows} cells near tribe 0's camp");
            return;
        }
        for (int t = 0; t < world.Tribes.Count; t++)
        {
            int placed = 0;
            foreach (Unit unit in world.UnitsOf(t))
            {
                if (!unit.InUse || placed == LineupLooks.Length)
                {
                    continue;
                }
                var cell = new GridPoint(origin.X + (placed * CellGap), origin.Y + (t * CellGap));
                Place(world, unit, cell, LineupLooks[placed]);
                placed++;
            }
        }
        lineupCentre = new GridPoint(origin.X + (columns / 2), origin.Y + (rows / 2));
        GD.Print($"WORLD_TEST lineup at {origin}: {world.Tribes.Count} tribes x {LineupLooks.Length} looks");
    }

    private static void Place(GameWorld world, Unit unit, GridPoint cell, (int Age, int Gender, int Pose, int Carried, int Weapon) look)
    {
        var from = new GridPoint(unit.X, unit.Y);
        if (world.Map.InPlay(from) && world.Map.CellAt(from) == MapCell.UnitAt(unit.Id))
        {
            world.Map.SetCell(from, MapCell.Empty);
        }
        (unit.X, unit.Y, unit.OffsetX, unit.OffsetY) = (cell.X, cell.Y, 0, 0);
        unit.BuildingSlot = -1;
        unit.ActionSubState = 0;
        unit.NextOccupantOrCellObject = 0;
        unit.Age = look.Age;
        unit.Gender = look.Gender;
        unit.Animation = look.Pose;
        unit.AnimationTick = 0;
        unit.Facing = LineupFacing;
        unit.CarriedItem = look.Carried;
        unit.CarriedAmount = look.Carried == 0 ? 0 : 3;
        if (look.Weapon >= 0)
        {
            unit.Weapon = look.Weapon;
        }
        world.Map.SetCell(cell, MapCell.UnitAt(unit.Id));
    }

    /// <summary>The top-left cell nearest <paramref name="near"/> of a grid (every other cell of <paramref name="width"/> by <paramref name="height"/>) of empty cells, or null.</summary>
    private static GridPoint? OpenGround(GameWorld world, GridPoint near, int width, int height)
    {
        for (int radius = 0; radius < 100; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == radius && IsOpen(world, near.X + dx, near.Y + dy, width, height))
                    {
                        return new GridPoint(near.X + dx, near.Y + dy);
                    }
                }
            }
        }
        return null;
    }

    private static bool IsOpen(GameWorld world, int x0, int y0, int width, int height)
    {
        for (int y = y0; y < y0 + height; y += CellGap)
        {
            for (int x = x0; x < x0 + width; x += CellGap)
            {
                var cell = new GridPoint(x, y);
                if (!world.Map.InPlay(cell) || !world.Map.CellAt(cell).IsEmpty)
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>The camp the camera starts over: <c>--look=T</c>, or the viewer's.</summary>
    private static GridPoint LookAt(GameWorld world, Dictionary<string, string> args, int viewer) =>
        world.Tribes[Math.Clamp(Number(args, "look", viewer), 0, world.Tribes.Count - 1)].Camp;
}
