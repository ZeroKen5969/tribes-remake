using System;
using System.Collections.Generic;
using Tribes.Core.Maps;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Model;

namespace Tribes.Client.World.Scene;

/// <summary>Units, animals and what is drawn on them (GameCanvas.a_IIIIB, p_, q_).</summary>
public sealed partial class ObjectLayer
{
    private const int ChiefCounter = 17;
    private const int OrderedTrapIcon = 65, ProtectionEffect = 69, DiseaseEffect = 73, FirstThought = 77;
    private const int ThoughtPeriod = 15;
    private const int SpellOrderKind = 3;
    private const int FirstTaskIcon = 577, FirstSkillStar = 602, StarSkill = 5;
    private const int PointerEffect = 41;
    private const int CastingSlot = 1, CastingAction = 51;
    private const string DigitSheet = "pi0_26";

    /// <summary>
    /// A unit standing on the map at (<paramref name="x"/>, <paramref name="y"/>) (its cell's corner
    /// plus its pixel offset): its body, then its overlays. A failure stops this unit only.
    /// </summary>
    private void DrawUnit(int id, int x, int y, int facing)
    {
        Unit unit = world.Unit(id);
        BodyComposition body;
        try
        {
            body = composers.Bodies.Compose(new BodyRequest(BodySubject.Villager, unit.AnimationNumber, facing, unit.AnimationTick)
            {
                Appearance = Appearance(unit, id),
                LungeFacing = unit.Facing,
                MapSelectionMarks = !world.Script.Active,
                PaletteVariant = variant,
            });
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        output.Add(body.Drawing.Parts, x, y, unit.Tribe, MoverKind.Unit, id);
        variant = body.Drawing.PaletteVariantAfter;
        if (!body.Drawing.Truncated && body.HeadAnchor is { } head && body.TorsoAnchor is { } torso)
        {
            DrawOverlaysSafely(id, x + head.X, y + head.Y, x + torso.X, y + torso.Y, false, MoverKind.Unit);
        }
    }

    /// <summary>An animal at (<paramref name="x"/>, <paramref name="y"/>) plus its offset, and its floating numbers.</summary>
    private void DrawAnimal(int index, int x, int y)
    {
        Animal animal = world.Animal(index);
        x += animal.OffsetX;
        y += animal.OffsetY;
        BodyComposition body;
        try
        {
            body = composers.Bodies.Compose(new BodyRequest(BodySubject.Animal, animal.Action & 0xFF, animal.Facing, animal.ActionFrame)
            {
                // The original moves a lunging animal by the facing of the unit with the animal's number.
                LungeFacing = index >= 1 && index <= world.UnitSlots ? world.Unit(index).Facing : 0,
                PaletteVariant = variant,
            });
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }
        output.Add(body.Drawing.Parts, x, y, -1, MoverKind.Animal, index);
        variant = body.Drawing.PaletteVariantAfter;
        if (!body.Drawing.Truncated && body.HeadAnchor is { } head && !world.Script.Active)
        {
            try
            {
                DrawLabels(FloatingLabels.AnimalKey(index), x + head.X + 4, y + head.Y, MoverKind.Animal, index, ownUnit: false);
            }
            catch (Exception e) when (IsDrawingFault(e))
            {
                // The animal's drawing stops here.
            }
        }
    }

    /// <summary>The appearance of <paramref name="unit"/> with the map's selection marks.</summary>
    private VillagerAppearance Appearance(Unit unit, int id)
    {
        WorldMarks marks = request.Marks;
        return new VillagerAppearance
        {
            Tribe = TribeLooks.OutfitOf(unit.Tribe),
            Age = unit.Age,
            Sex = (Sex)unit.Gender,
            IsLeader = id == world.Tribes[unit.Tribe].Counter(ChiefCounter) && sight.ShowsMarksOf(unit.Tribe),
            AssignedTask = unit.AssignedTask,
            Weapon = unit.Weapon,
            CarriedItem = unit.CarriedItem,
            CarriedAmount = unit.CarriedAmount,
            InSelectedGroup = marks.SelectedUnits.Contains(id),
            Hovered = id == marks.HoveredUnit,
            InSelectionRectangle = marks.BoxedUnits.Contains(id),
        }.WithAppearanceBits(unit.Appearance);
    }

    /// <summary>The overlays of a unit; a failure stops them, as in the original.</summary>
    private void DrawOverlaysSafely(int id, int headX, int headY, int torsoX, int torsoY, bool occupant, MoverKind mover)
    {
        if (world.Script.Active)
        {
            return;
        }
        try
        {
            DrawOverlays(id, headX, headY, torsoX, torsoY, occupant, mover);
        }
        catch (Exception e) when (IsDrawingFault(e))
        {
            // The unit's drawing stops here.
        }
    }

    /// <summary>
    /// What is drawn on a unit: floating numbers, ordered spells, thoughts, status effects, and for
    /// the viewer's units the task icon and the health bar with the skill star.
    /// </summary>
    private void DrawOverlays(int id, int headX, int headY, int torsoX, int torsoY, bool occupant, MoverKind mover)
    {
        Unit unit = world.Unit(id);
        int moverId = mover == MoverKind.None ? 0 : id;
        DrawLabels(FloatingLabels.UnitKey(id), headX + 4, headY, mover, moverId, sight.IsOwn(unit.Tribe));
        // A player sees its own task orders over any unit, as the original does; a spectator each human tribe's over its units.
        int orderer = sight.Spectating ? (sight.ShowsMarksOf(unit.Tribe) ? unit.Tribe : -1) : sight.Home;
        if (orderer >= 0)
        {
            foreach (TaskOrder order in world.Tribes[orderer].TaskOrders)
            {
                if (order.A == id && order.Kind != SpellOrderKind)
                {
                    AddEffect(OrderedTrapIcon + ((1 - order.Extra) * 5) + order.Kind, torsoX, torsoY, presentation.Cycle, mover, moverId);
                    break;
                }
            }
        }
        if (unit.BuildingSlot == CastingSlot && unit.Action == CastingAction && unit.ActionSubState > 0)
        {
            AddEffect(OrderedTrapIcon, torsoX, torsoY, presentation.Cycle, mover, moverId);
        }
        if (unit.Thought > -1)
        {
            AddEffect(FirstThought + unit.Thought, headX, headY, (world.Frame - (id & 7)) % ThoughtPeriod, mover, moverId);
        }
        if (unit.BuildingSlot < -1)
        {
            AddEffect(-unit.BuildingSlot, torsoX, torsoY, unit.AnimationTick, mover, moverId);
        }
        if ((unit.Status & UnitStatus.Diseased) != 0)
        {
            AddEffect(DiseaseEffect, headX, headY, presentation.Cycle + id, mover, moverId);
        }
        if ((unit.Status & UnitStatus.Protected) != 0)
        {
            AddEffect(ProtectionEffect, torsoX, torsoY, presentation.Cycle, mover, moverId);
        }
        if (sight.IsOwn(unit.Tribe))
        {
            DrawOwnUnitMarks(unit, id, headX, headY, occupant, mover, moverId);
        }
        if (id == request.Marks.PointedUnit)
        {
            AddEffect(PointerEffect, torsoX - 1, torsoY - 16 - 5, presentation.Blink, mover, moverId);
        }
    }

    /// <summary>The task icon and the health bar of the viewer's unit.</summary>
    private void DrawOwnUnitMarks(Unit unit, int id, int headX, int headY, bool occupant, MoverKind mover, int moverId)
    {
        WorldMarks marks = request.Marks;
        bool focused = marks.FocusedUnits.Contains(id) || id == marks.HoveredUnit || CellCode.OfUnit(id) == cursorCode
            || (occupant && cursorCode != int.MinValue && CellCode.BuildingOf(cursorCode) is > 0 and int building && building == unit.TargetIndex);
        if (marks.TaskIconMode == 0 || (marks.TaskIconMode == 1 && focused))
        {
            AddSprite(FirstTaskIcon + (marks.TaskIcons.TryGetValue(id, out int icon) ? icon : 0), headX, headY, -1, mover, moverId);
        }
        int hp = unit.HitPoints;
        int max = unit.MaxHitPoints;
        if (marks.HideHealthBars || hp <= 0 || !(focused || max / 3 * 2 > hp))
        {
            return;
        }
        int bar = max / 20;
        int shift = 0;
        int ranged = unit.RangedSkill, melee = unit.MeleeSkill, scouting = unit.ScoutingSkill;
        if (ranged >= StarSkill || melee >= StarSkill || scouting > StarSkill)
        {
            shift = 2;
            int best = ranged > melee
                ? (ranged > scouting ? ranged : (melee >= scouting ? melee : scouting - 1))
                : (melee >= scouting ? melee : scouting - 1);
            AddSprite(FirstSkillStar + best - StarSkill, headX + 2 - bar + 2 - 10, headY, -1, mover, moverId);
        }
        AddSprite(FirstHealthBarSprite + bar, headX + shift, headY, -1, mover, moverId);
        RgbColor colour = SceneAssets.HealthColours[(hp - 1) * 3 / max];
        IReadOnlyList<int> widths = world.Content.Rules.Interface.HitPointBarWidths;
        output.Add(DrawPart.Fill(headX + 2 - bar + shift, headY - 4, widths[bar] * hp / max, HealthBarHeight, colour, -1), -1, mover, moverId);
    }

    /// <summary>
    /// The floating numbers over <paramref name="key"/> (see <see cref="FloatingLabels.UnitKey"/>),
    /// centred on <paramref name="x"/>; a loss shows red over the viewer's own units (the original's
    /// player is tribe 0, whose units are 1..50) and yellow over the others.
    /// </summary>
    private void DrawLabels(int key, int x, int y, MoverKind mover, int moverId, bool ownUnit)
    {
        FloatingLabels labels = presentation.Labels;
        for (int k = labels.First; k != labels.End; k = (k + 1) % FloatingLabels.RingSize)
        {
            FloatingLabels.Label label = labels.Ring[k];
            if (label.Key != key || label.Text == null)
            {
                continue;
            }
            int pen = x - (LabelWidth(label.Text) / 2) + ((presentation.Jitter.NextInt() & 0xFF) % 5) - 2;
            int top = y - 5 - (label.Rise * 2);
            BitmapFont font = assets.SmallFont;
            foreach (char c in label.Text)
            {
                int code = assets.Fonts.GlyphByByte[c & 0x7F];
                int row = code / 30;
                Glyph glyph = assets.SmallGlyph(code);
                GlyphRow glyphRow = font.Rows[row];
                int colour = label.Colour == FloatingLabels.Positive ? label.Colour : ownUnit ? FloatingLabels.PlayerUnit : FloatingLabels.Other;
                int sourceY = glyphRow.Top + (font.Rows[0].Height * colour);
                if (glyph.FitsSheet && sourceY + glyphRow.Height <= DigitSheetHeight)
                {
                    output.Add(DrawPart.Image(DigitSheet, glyph.X, sourceY, glyph.Width, glyphRow.Height, pen, top, -1), -1, mover, moverId);
                }
                pen += glyph.Width + 1;
            }
        }
    }

    private const int DigitSheetHeight = 24;

    /// <summary>The width the original gives a floating number: its glyphs plus one pixel between them.</summary>
    private int LabelWidth(string text)
    {
        int width = text.Length - 1;
        foreach (char c in text)
        {
            int code = assets.Fonts.GlyphByByte[c & 0x7F];
            if (code == assets.Fonts.NarrowBackspaceCode)
            {
                width -= 4;
            }
            else if (code / 30 != 0 || code % 30 < 14)
            {
                width += assets.SmallGlyph(code).Width;
            }
        }
        return width;
    }
}
