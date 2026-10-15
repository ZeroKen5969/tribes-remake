using System;
using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Client.World.Scene;

/// <summary>
/// Smooth motion between rule frames: remembers where each unit, animal and projectile was at the
/// last two rule frames (in map pixels) and gives the offset that draws it part of the way from
/// the older place to the newer one. Moves longer than a couple of cells (a unit leaving a
/// building, a slot reused) jump as the original does.
/// </summary>
public sealed class MotionTracker
{
    private const int MaxSmoothX = 2 * 22, MaxSmoothY = 2 * 16;

    private Dictionary<(MoverKind, int), (int X, int Y)> previous = new();
    private Dictionary<(MoverKind, int), (int X, int Y)> current = new();

    /// <summary>Remembers where everything is after a rule frame.</summary>
    public void OnRuleFrame(GameWorld world, ScenePresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(presentation);
        (previous, current) = (current, previous);
        current.Clear();
        const int CellWidth = 22, CellHeight = 16;
        for (int id = 1; id <= world.UnitSlots; id++)
        {
            Unit unit = world.Unit(id);
            if (unit.InUse)
            {
                current[(MoverKind.Unit, id)] = ((unit.X * CellWidth) + unit.OffsetX, (unit.Y * CellHeight) + unit.OffsetY);
            }
            if (unit.ProjectileFlightSteps > 0)
            {
                int height = presentation.Arcs.TryGetValue(id, out ProjectileArc? arc) && unit.ProjectileStep >= 0
                    && unit.ProjectileStep < arc.Heights.Count ? arc.Heights[unit.ProjectileStep] : 0;
                current[(MoverKind.Projectile, id)] = ((unit.ProjectileX * CellWidth) + unit.ProjectileSubX,
                    (unit.ProjectileY * CellHeight) + unit.ProjectileSubY - height);
            }
        }
        for (int i = 0; i < world.AnimalSlots; i++)
        {
            Animal animal = world.Animal(i);
            if (animal.InUse)
            {
                current[(MoverKind.Animal, i)] = ((animal.X * CellWidth) + animal.OffsetX, (animal.Y * CellHeight) + animal.OffsetY);
            }
        }
    }

    /// <summary>A number that changes when any drawing offset at <paramref name="progress"/> changes (to skip redrawing still frames).</summary>
    public long OffsetsKey(float progress)
    {
        long key = 17;
        foreach (var (mover, id) in current.Keys)
        {
            (int x, int y) = Offset(mover, id, progress);
            key = unchecked((key * 31) + ((long)mover << 40) + ((long)id << 20) + (x * 1024) + y);
        }
        return key;
    }

    /// <summary>Forgets the old places (after a jump in time such as loading a game).</summary>
    public void Reset()
    {
        previous.Clear();
        current.Clear();
    }

    /// <summary>
    /// The offset (in pixels, rounded) to draw <paramref name="mover"/> with when
    /// <paramref name="progress"/> (0..1) of the time to the next rule frame has passed.
    /// </summary>
    public (int X, int Y) Offset(MoverKind mover, int id, float progress)
    {
        if (mover == MoverKind.None || !current.TryGetValue((mover, id), out var now) || !previous.TryGetValue((mover, id), out var before))
        {
            return (0, 0);
        }
        int dx = before.X - now.X, dy = before.Y - now.Y;
        if (Math.Abs(dx) > MaxSmoothX || Math.Abs(dy) > MaxSmoothY)
        {
            return (0, 0);
        }
        float remaining = 1f - Math.Clamp(progress, 0f, 1f);
        return ((int)MathF.Round(dx * remaining), (int)MathF.Round(dy * remaining));
    }
}
