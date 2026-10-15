using System;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.World;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Dev;

/// <summary>
/// Finds things on the map for scripted checks and brings them on screen: the nearest resource
/// node, animal, enemy or open ground to the selection, and a free place for the ghost.
/// </summary>
public static class ScriptTargets
{
    private const int SearchRadius = 16;

    /// <summary>The screen point of the nearest <paramref name="what"/> (node, animal, enemy, building, ground) to the selection.</summary>
    public static Vector2 Nearest(MatchScreen screen, string what)
    {
        ArgumentNullException.ThrowIfNull(screen);
        GameWorld world = screen.World;
        GridPoint from = Origin(screen);
        FogOfWar fog = world.Fog(screen.ViewTribe);
        GridPoint? best = null;
        int bestDistance = int.MaxValue;
        void Consider(GridPoint cell)
        {
            int distance = Math.Abs(cell.X - from.X) + Math.Abs(cell.Y - from.Y);
            if (world.Map.InPlay(cell) && distance < bestDistance)
            {
                bestDistance = distance;
                best = cell;
            }
        }
        switch (what)
        {
            case "node":
                foreach (ResourceNode node in world.Map.ResourceNodes)
                {
                    if (node.InUse && node.Amount > 0 && fog.IsVisible(node.Position))
                    {
                        Consider(node.Position);
                    }
                }
                break;
            case "animal":
                for (int i = 0; i < world.AnimalSlots; i++)
                {
                    Animal animal = world.Animal(i);
                    if (animal.HitPoints > 0 && fog.IsVisible(new GridPoint(animal.X, animal.Y)))
                    {
                        Consider(new GridPoint(animal.X, animal.Y));
                    }
                }
                break;
            case "enemy":
                for (int id = 1; id <= world.UnitSlots; id++)
                {
                    Unit unit = world.Unit(id);
                    if (unit.HitPoints > 0 && unit.Tribe != screen.Tribe && fog.IsVisible(new GridPoint(unit.X, unit.Y)))
                    {
                        Consider(new GridPoint(unit.X, unit.Y));
                    }
                }
                break;
            default:
                for (int r = 3; r < SearchRadius && best is null; r++)
                {
                    var cell = new GridPoint(from.X + r, from.Y);
                    if (world.Map.InPlay(cell) && world.Map.CellAt(cell).IsEmpty && fog.IsVisible(cell))
                    {
                        Consider(cell);
                    }
                }
                break;
        }
        GridPoint target = best ?? throw new InvalidOperationException($"no {what} in view");
        screen.CameraRig.CentreOnCell(target.X, target.Y);
        return screen.CameraRig.MapToScreen(new Vector2((target.X + 0.5f) * WorldView.CellWidth, (target.Y + 0.5f) * WorldView.CellHeight));
    }

    /// <summary>The screen point that puts the ghost of <paramref name="kind"/> on the nearest free footprint.</summary>
    public static Vector2 FreeFootprint(MatchScreen screen, int kind)
    {
        ArgumentNullException.ThrowIfNull(screen);
        GameWorld world = screen.World;
        GridPoint start = screen.GhostOrigin;
        (int width, int height) = FootprintCheck.Size(world, kind);
        for (int r = 0; r < SearchRadius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }
                    var origin = new GridPoint(start.X + dx, start.Y + dy);
                    if (FootprintCheck.IsFree(world, Math.Max(0, screen.Tribe), kind, origin))
                    {
                        var point = new Vector2((origin.X + ((width - 1) / 2f) + 0.5f) * WorldView.CellWidth,
                            (origin.Y + ((height - 1) / 2f) + 0.5f) * WorldView.CellHeight);
                        screen.CameraRig.CentreOn(point);
                        return screen.CameraRig.MapToScreen(point);
                    }
                }
            }
        }
        throw new InvalidOperationException($"no free place for kind {kind}");
    }

    private static GridPoint Origin(MatchScreen screen)
    {
        if (screen.Selection.Units.Count > 0)
        {
            Unit unit = screen.World.Unit(screen.Selection.Units[0]);
            return new GridPoint(unit.X, unit.Y);
        }
        Vector2 centre = screen.CameraRig.ScreenToMap(screen.GetViewportRect().Size / 2);
        return new GridPoint((int)(centre.X / WorldView.CellWidth), (int)(centre.Y / WorldView.CellHeight));
    }
}
