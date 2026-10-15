using System;
using System.Collections.Generic;
using Tribes.Core.Maps;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Model;

namespace Tribes.Client.World.Scene;

/// <summary>Alarm and trap-order markers, projectiles, the effect list and dust clouds (GameCanvas.v_, i_I, r_, s_).</summary>
public sealed partial class ObjectLayer
{
    private const int AlarmEffect = 68;
    private const int OrderedTrapKind = 3;
    private const int DustEffect = 74, DustFrames = 20;
    private const int UnitSubject = 1, AnimalSubject = 2;
    private static readonly int[] NeighbourX = { -1, 0, 1, -1, 0, 1, -1, 0, 1 };
    private static readonly int[] NeighbourY = { -1, -1, -1, 0, 0, 0, 1, 1, 1 };

    /// <summary>
    /// Where a tribe's alarm was raised, the visible cells around it blink: the alarms of each tribe
    /// whose marks the viewer sees. The original walks the player's five alarms and then, by a slip in
    /// its loop, the computer's alarms 1..4.
    /// </summary>
    private void DrawAlarms()
    {
        IReadOnlyList<int> tribes = sight.Tribes;
        for (int i = 0; i < tribes.Count; i++)
        {
            if (sight.ShowsMarksOf(tribes[i]))
            {
                DrawAlarmsOf(tribes[i], 0, tribes[i]);
            }
        }
        if (sight.Home == 0 && !sight.Spectating && world.Tribes.Count > 1)
        {
            DrawAlarmsOf(1, 1, 0);
        }
    }

    private void DrawAlarmsOf(int tribe, int firstSlot, int viewer)
    {
        AlarmCall[] alarms = world.Tribes[tribe].Alarms;
        for (int slot = firstSlot; slot < alarms.Length; slot++)
        {
            AlarmCall alarm = alarms[slot];
            if (alarm.Age < 0 || !alarm.IsKnownTo(viewer) || !NearView(alarm.X, alarm.Y))
            {
                continue;
            }
            for (int n = 0; n < NeighbourX.Length; n++)
            {
                int x = alarm.X + NeighbourX[n];
                int y = alarm.Y + NeighbourY[n];
                if (FogAt(x, y) == Visible)
                {
                    AddEffect(AlarmEffect, (x - view.Column) * cellWidth, (y - view.Row) * cellHeight, presentation.Cycle);
                }
            }
        }
    }

    /// <summary>Cells where a tribe whose marks the viewer sees ordered a trap set blink until it is set.</summary>
    private void DrawTrapOrders()
    {
        IReadOnlyList<int> tribes = sight.Tribes;
        for (int i = 0; i < tribes.Count; i++)
        {
            if (!sight.ShowsMarksOf(tribes[i]))
            {
                continue;
            }
            foreach (TaskOrder order in world.Tribes[tribes[i]].TaskOrders)
            {
                if (order.Kind == OrderedTrapKind && order.Building != 0 && NearView(order.A, order.B) && FogAt(order.A, order.B) == Visible)
                {
                    AddEffect(AlarmEffect + ((1 - order.Extra) * 5), (order.A - view.Column) * cellWidth, (order.B - view.Row) * cellHeight,
                        presentation.Cycle);
                }
            }
        }
    }

    /// <summary>Whether a cell is within one cell of the view (as far as the original looks for markers).</summary>
    private bool NearView(int x, int y) =>
        x >= view.Column - 1 && x <= view.Column + view.Columns + 1 && y >= view.Row - 1 && y <= view.Row + view.Rows + 1;

    /// <summary>
    /// The projectiles in flight inside the view, listed per cell: units in slot order, each cell's
    /// list newest first (World.T_).
    /// </summary>
    private void BuildProjectileLists()
    {
        if (projectileHeads.GetLength(0) != view.Rows + 3 || projectileHeads.GetLength(1) != view.Columns)
        {
            projectileHeads = new int[view.Rows + 3, view.Columns];
        }
        else
        {
            Array.Clear(projectileHeads);
        }
        if (projectileNext.Length != world.UnitSlots + 1)
        {
            projectileNext = new int[world.UnitSlots + 1];
        }
        for (int id = 1; id <= world.UnitSlots; id++)
        {
            Unit unit = world.Unit(id);
            projectileNext[id] = 0;
            if (unit.ProjectileFlightSteps > 0 && view.Contains(unit.ProjectileX, unit.ProjectileY))
            {
                int row = unit.ProjectileY - view.Row, col = unit.ProjectileX - view.Column;
                projectileNext[id] = projectileHeads[row, col];
                projectileHeads[row, col] = id;
            }
        }
    }

    /// <summary>The projectiles listed on one cell, high on their arcs. A failure stops the list.</summary>
    private void DrawProjectiles(int first)
    {
        const int Stone = 1, Spear = 2, Tomahawk = 5, LiftAboveCentre = 10;
        try
        {
            int guard = world.UnitSlots;
            for (int id = first; id > 0 && guard-- > 0; id = projectileNext[id])
            {
                Unit unit = world.Unit(id);
                ProjectileArc arc = ArcFor(unit) ?? throw new DrawFailedException("no arc for projectile " + id);
                int step = unit.ProjectileStep;
                int frameOffset = arc.Frames[step];
                int x = ((unit.ProjectileX - view.Column) * cellWidth) + (cellWidth / 2) + unit.ProjectileSubX;
                int y = ((unit.ProjectileY - view.Row) * cellHeight) + (cellHeight / 2) + unit.ProjectileSubY - arc.Heights[step] - LiftAboveCentre;
                if (unit.Weapon == Stone || unit.Weapon == Spear || unit.Weapon == Tomahawk)
                {
                    int sprite = composers.Sprites.ProjectileSprite(unit.Weapon, unit.ProjectileFacing, frameOffset);
                    AddSprite(sprite, x, y, -1, MoverKind.Projectile, id);
                }
            }
        }
        catch (Exception e) when (IsDrawingFault(e))
        {
            // The rest of this cell's projectiles are not drawn.
        }
    }

    /// <summary>The arc of a unit's projectile: the one laid out at the throw, or worked out from the unit now.</summary>
    private ProjectileArc? ArcFor(Unit unit)
    {
        if (presentation.Arcs.TryGetValue(unit.Id, out ProjectileArc? arc))
        {
            return arc;
        }
        (int height, int drop) = ScenePresentation.ThrowHeights(unit.Action);
        return ScenePresentation.ArcOf(world, unit, height, drop);
    }

    /// <summary>
    /// The effect list over everything: each effect follows its unit (at the slot it is attached
    /// to), animal or building cell; an effect whose subject is out of sight ends, and as in the
    /// original the effects after it wait for the next picture.
    /// </summary>
    private void DrawEffects()
    {
        EffectSlot[] effects = presentation.Effects;
        try
        {
            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i].Effect <= 0)
                {
                    continue;
                }
                if (!EffectPosition(ref effects[i], out int x, out int y))
                {
                    effects[i].Effect = 0;
                    return;
                }
                AddEffect(effects[i].Effect, x, y, effects[i].Frame);
                if (request.AdvanceEffects)
                {
                    effects[i].Frame++;
                    if (effects[i].Frame >= assets.Catalog.Effects[effects[i].Effect].Frames.Count)
                    {
                        effects[i].Effect = 0;
                    }
                }
            }
        }
        catch (Exception e) when (IsDrawingFault(e))
        {
            // The rest of the list waits for the next picture.
        }
    }

    /// <summary>Where an effect is drawn now; false when its subject is out of the view, fogged or (a unit) dead.</summary>
    private bool EffectPosition(ref EffectSlot effect, out int x, out int y)
    {
        int slot = effect.Slot;
        int cellX, cellY;
        bool unitSubject = effect.Subject == UnitSubject;
        if (unitSubject)
        {
            Unit unit = world.Unit(slot);
            (cellX, cellY) = (unit.X, unit.Y);
        }
        else if (effect.Subject == AnimalSubject)
        {
            Animal animal = world.Animal(slot);
            (cellX, cellY) = (animal.X, animal.Y);
            effect.Anchor = 0;
        }
        else
        {
            Building building = world.Building(slot);
            (cellX, cellY) = (building.X + (effect.Anchor & 15), building.Y + ((effect.Anchor >> 4) & 15));
        }
        x = 0;
        y = 0;
        if (!view.Contains(cellX, cellY) || FogAt(cellX, cellY) != Visible || (unitSubject && world.Unit(slot).HitPoints <= 0))
        {
            return false;
        }
        x = (cellX - view.Column) * cellWidth;
        y = (cellY - view.Row) * cellHeight;
        if (unitSubject)
        {
            Unit unit = world.Unit(slot);
            AnchorPoint at = SlotOffset(unit.AnimationNumber, unit.Facing, unit.AnimationTick, effect.Anchor);
            x += unit.OffsetX + at.X;
            y += unit.OffsetY + at.Y;
        }
        else if (effect.Subject == AnimalSubject)
        {
            Animal animal = world.Animal(slot);
            // The original takes the facing of the unit with the animal's number here too.
            int facing = slot >= 1 && slot <= world.UnitSlots ? world.Unit(slot).Facing : 0;
            AnchorPoint at = SlotOffset(animal.Action & 0xFF, facing, animal.ActionFrame, effect.Anchor);
            x += animal.OffsetX + at.X;
            y += animal.OffsetY + at.Y;
        }
        return true;
    }

    /// <summary>The offset of the first pose slot of type <paramref name="slotType"/> (the last slot when none).</summary>
    private AnchorPoint SlotOffset(int animation, int facing, int frame, int slotType)
    {
        if (facing < 0 || facing >= BodyComposer.FacingCount)
        {
            throw new IndexOutOfRangeException("facing " + facing);
        }
        try
        {
            return composers.Bodies.EffectSlotOffset(animation, facing, frame, (SlotType)slotType);
        }
        catch (Exception e) when (e is not ArgumentNullException)
        {
            throw new DrawFailedException("no pose for an effect", e);
        }
    }

    /// <summary>The dust over buildings going up, upgraded or repaired: a puff on every other cell, 20 frames long.</summary>
    private void DrawDust()
    {
        foreach (DustCloud cloud in world.Dust)
        {
            if (cloud.Building == 0 || cloud.Frames >= DustFrames)
            {
                continue;
            }
            Building building = world.Building(cloud.Building);
            if (remembering && !ShowsLive(cloud.Building))
            {
                // Work on a building out of sight does not show in a match.
                continue;
            }
            var kind = world.Content.Rules.Buildings.Kinds[building.Kind];
            int top = building.Y - 1;
            for (int row = 0; row < kind.Height + 1; row += 2)
            {
                for (int col = 0; col < kind.Width; col += 2)
                {
                    if (view.Contains(building.X + col, top + row))
                    {
                        AddEffect(DustEffect, (building.X + col - view.Column) * cellWidth, (top + row - view.Row) * cellHeight,
                            (((col * 2) + row) * 2) + cloud.Frames - 1);
                    }
                }
            }
        }
    }
}
