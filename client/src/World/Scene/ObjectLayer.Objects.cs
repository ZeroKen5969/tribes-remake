using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Model;

namespace Tribes.Client.World.Scene;

/// <summary>Buildings, resource nodes and traps in the object layer (GameCanvas.a_IB / a_IBBIIB).</summary>
public sealed partial class ObjectLayer
{
    private const int FirstComputerSlot = 31;
    private const int OccupantGroups = 8;
    private const int OccupantGroupSlots = 11;
    private const int NeedsWorkerEffect = 40;
    private const int FirstHealthBarSprite = 607;
    private const int HealthBarHeight = 2;
    private const int FirstSiteKind = 12;
    private const int LastMarkedKind = 21;
    private const int ChiefHutKind = 3;
    private const int MissionWithHeadquarters = 3, HeadquartersSlot = 31;
    private const int FlagEffect = 106, BuildingDustEffect = 110, AmbientClockPeriod = 120;
    private const int UpgradingMode = 1, RepairingMode = 2;
    private static readonly RgbColor SlotUsedColour = RgbColor.Parse("#f6a800");
    private static readonly RgbColor SlotFreeColour = RgbColor.Parse("#40331d");

    // Reused by every building of a composition (the composers read them before they return).
    private readonly int[] occupantGroups = new int[OccupantGroupSlots];
    private readonly List<(int Group, int Unit)> buildingOccupants = new();
    private readonly List<Occupant> occupantList = new();
    private readonly Dictionary<int, int> unitOfGroup = new();
    private int[] viewerStock = Array.Empty<int>();

    /// <summary>
    /// A building's cell: what the scan has seen of the building decides how fogged it looks (and
    /// the fog over this cell); the first cell of it the scan reaches draws it with its markers.
    /// Returns the fog to draw over the cell.
    /// </summary>
    private int DrawBuildingCell(int slot, int code, int fog, int y, int mapRow)
    {
        int seen = visibility[slot];
        if ((seen & 4) != 0)
        {
            if (fog == Visible)
            {
                seen = 6;
            }
            else if (fog == Explored)
            {
                seen = 5;
            }
            visibility[slot] = seen;
        }
        switch (seen & 3)
        {
            case 1:
                fog = Explored;
                break;
            case 2:
                fog = Visible;
                break;
            default:
                break;
        }
        if (!drawn[slot])
        {
            DrawBuilding(slot, mapRow);
            drawn[slot] = true;
            DrawBuildingMarkers(slot, code, y);
        }
        return fog;
    }

    /// <summary>The building's upper parts and its occupants.</summary>
    private void DrawBuilding(int slot, int mapRow)
    {
        Building building = world.Building(slot);
        bool own = sight.IsOwn(world.TribeOfBuilding(slot));
        if (AllianceHall.Is(building.Kind))
        {
            DrawAllianceHall(building.X, building.Y, AllianceHall.FrameOf(building), (int)building.WorkMode, own, mapRow);
            return;
        }
        int[] groups = occupantGroups;
        Array.Clear(groups);
        int guard = world.UnitSlots;
        for (int unit = building.FirstOccupant; unit > 0 && guard-- > 0; unit = world.Unit(unit).NextOccupantOrCellObject)
        {
            int group = world.Unit(unit).BuildingSlot;
            if (group < 0 || group >= OccupantGroupSlots)
            {
                throw new IndexOutOfRangeException($"occupant group {group}");
            }
            groups[group] = unit;
        }
        List<(int Group, int Unit)> occupants = buildingOccupants;
        occupants.Clear();
        for (int g = 0; g < OccupantGroups; g++)
        {
            if (groups[g] > 0)
            {
                occupants.Add((g, groups[g]));
            }
        }
        ObjectRequest objectRequest = ObjectRequest.Building(building.Kind, building.Level, own ? 1 : FirstComputerSlot, ObjectPass.Upper) with
        {
            Tick = presentation.Cycle,
            MapRow = mapRow,
            SlotWorkMode = (int)building.WorkMode,
            SlotBuildingKind = building.Kind,
            OccupantsVisible = own || (visibility[slot] & 3) >= 2,
            PlayerStock = viewerStock,
            MissionThreeHeadquarters = world.Mission == MissionWithHeadquarters && slot == HeadquartersSlot,
            MapSelectionMarks = !world.Script.Active,
        };
        DrawObject(objectRequest, (building.X - view.Column) * cellWidth, (building.Y - view.Row) * cellHeight, occupants);
    }

    /// <summary>
    /// The alliance hall: its picture for how it stands (whole, damaged, ruin), then like the
    /// original's buildings the flag (the viewer's or the others') and, while it is repaired, the dust.
    /// </summary>
    private void DrawAllianceHall(int cellX, int cellY, int frame, int mode, bool own, int mapRow)
    {
        AllianceHallArt art = assets.AllianceHall;
        int x = (cellX - view.Column) * cellWidth, y = (cellY - view.Row) * cellHeight;
        output.Add(DrawPart.Image(AllianceHall.SheetId, frame * art.FrameWidth, 0, art.FrameWidth, art.FrameHeight,
            x - art.AnchorX, y - art.AnchorY, -1));
        // The others' flag and dust are the effects after the viewer's, as the original picks them.
        int other = own ? 0 : 1;
        int clock = presentation.Cycle, part = 0;
        if (frame != AllianceHall.Ruin)
        {
            clock = AmbientClock(clock, mapRow, part++);
            AddEffect(FlagEffect + other, x + art.Flag.X, y + art.Flag.Y, clock);
        }
        if (mode == UpgradingMode || mode == RepairingMode)
        {
            foreach ((int dx, int dy) in art.DustSpots)
            {
                clock = AmbientClock(clock, mapRow, part++);
                AddEffect(BuildingDustEffect + other, x + dx, y + dy, clock);
            }
        }
    }

    /// <summary>
    /// A building out of sight as the viewer remembers it: its picture as it was (no one is shown
    /// inside it, nor its health bar or other marks).
    /// </summary>
    private void DrawRemembered(RememberedBuilding seen, int mapRow)
    {
        bool own = sight.IsOwn(seen.Tribe);
        if (AllianceHall.Is(seen.Kind))
        {
            DrawAllianceHall(seen.X, seen.Y, AllianceHall.FrameOf(seen.HitPoints, seen.MaxHitPoints), seen.WorkMode, own, mapRow);
            return;
        }
        ObjectRequest objectRequest = ObjectRequest.Building(seen.Kind, seen.Level, own ? 1 : FirstComputerSlot, ObjectPass.Upper) with
        {
            Tick = presentation.Cycle,
            MapRow = mapRow,
            SlotWorkMode = seen.WorkMode,
            SlotBuildingKind = seen.Kind,
            OccupantsVisible = false,
            PlayerStock = viewerStock,
            MapSelectionMarks = !world.Script.Active,
        };
        buildingOccupants.Clear();
        DrawObject(objectRequest, (seen.X - view.Column) * cellWidth, (seen.Y - view.Row) * cellHeight, buildingOccupants);
    }

    /// <summary>
    /// The map row an object's animation clocks start from: the row itself, or on an endless map,
    /// whose rows can be negative, the same row of the clocks' 120-frame period counted from 0
    /// (the composer counts the clocks up from the row).
    /// </summary>
    private int ClockRow(int mapRow) => chunked ? ((mapRow % AmbientClockPeriod) + AmbientClockPeriod) % AmbientClockPeriod : mapRow;

    /// <summary>The clock of an object's next always-playing animation, moved on as the object composer moves it.</summary>
    private static int AmbientClock(int clock, int mapRow, int part)
    {
        clock += mapRow + (part * 2);
        clock %= AmbientClockPeriod;
        return clock < 0 ? clock + AmbientClockPeriod : clock;
    }

    /// <summary>The "needs a worker" marker, the health bar and, under the cursor, the occupancy squares.</summary>
    private void DrawBuildingMarkers(int slot, int code, int y)
    {
        Building building = world.Building(slot);
        int kind = building.Kind;
        bool hall = AllianceHall.Is(kind);
        if (world.Script.Active || (kind > LastMarkedKind && !hall) || !sight.IsOwn(world.TribeOfBuilding(slot)))
        {
            return;
        }
        BuildingKind definition = world.Content.Rules.Buildings.Kinds[kind];
        int mode = (int)building.WorkMode;
        bool busy = mode == 1 || mode == 2;
        int left = ((building.X - view.Column) * cellWidth) + (definition.Width * (cellWidth / 2)) - (cellWidth / 2);
        if ((definition.ManagerSlot != -1 || busy) && building.OperatorOrBeds == 0)
        {
            AddEffect(NeedsWorkerEffect, left,
                ((building.Y - view.Row) * cellHeight) + (definition.Height * (cellHeight / 2)) - (cellHeight / 2), presentation.Blink);
        }
        int hp = building.HitPoints;
        int max = building.MaxHitPoints;
        bool cursorOn = code == cursorCode;
        bool shown = hall
            ? hp > 0 && max > 0 && (cursorOn || max / 2 > hp || busy)
            : (hp > 0 && (cursorOn || max / 2 > hp)) || busy || kind >= FirstSiteKind;
        if (shown)
        {
            IReadOnlyList<int> widths = world.Content.Rules.Interface.HitPointBarWidths;
            int bar = (max / 80) + 6;
            if (hall)
            {
                // Its hit points are not one of the original's, so the longest bar is the limit.
                bar = Math.Min(bar, widths.Count - 1);
            }
            int top = y - (definition.HitPointBarOffset * 8) - 5;
            AddSprite(FirstHealthBarSprite + bar, left, top);
            int shade = (hp - 1) * 3 / max;
            RgbColor colour = SceneAssets.HealthColours[hall ? Math.Clamp(shade, 0, SceneAssets.HealthColours.Length - 1) : shade];
            output.Add(DrawPart.Fill(left + 1, top + 1, widths[bar] * (hall ? Math.Min(hp, max) : hp) / max, HealthBarHeight, colour, -1));
            if (cursorOn && !hall)
            {
                DrawOccupancy(slot, left + 1 + (widths[bar] / 2), top + 6);
            }
        }
    }

    /// <summary>The squares under a building's health bar: places taken and still free.</summary>
    private void DrawOccupancy(int slot, int centre, int top)
    {
        int counts = Occupancy(slot);
        int free = (counts >> 4) & 15;
        int used = counts & 15;
        int total = free + used;
        if (total <= 0)
        {
            return;
        }
        int x = centre - (4 * total / 2) + 1;
        output.Add(DrawPart.Fill(x, top - 1, (total * 4) - 1, 4, Black, -1));
        output.Add(DrawPart.Fill(x - 1, top, (total * 4) + 1, 2, Black, -1));
        for (int i = 0; i < total; i++)
        {
            output.Add(DrawPart.Fill(x + (i * 4), top, 3, 2, i < used ? SlotUsedColour : SlotFreeColour, -1));
        }
    }

    /// <summary>Places used (low 4 bits) and free (high 4 bits) in a building, as the original counts them.</summary>
    private int Occupancy(int slot)
    {
        Building building = world.Building(slot);
        BuildingKind definition = world.Content.Rules.Buildings.Kinds[building.Kind];
        int used = building.OccupantCount;
        int op = building.OperatorOrBeds;
        if ((definition.ManagerSlot > -1 || building.WorkMode != 0) && op > 0
            && (world.Unit(op).BuildingSlot < 0 || world.Unit(op).TargetIndex != slot))
        {
            used++;
        }
        int places = building.WorkMode == 0
            ? (building.Kind != ChiefHutKind ? definition.WorkerSlots[building.Level - 1] : 1)
            : definition.WorkerSlotsWhileBusy;
        return unchecked((sbyte)(used + (Math.Clamp(places - used, 0, 7) << 4)));
    }

    /// <summary>A resource node (the original reads the building slot with the node's number for its dust and hearts).</summary>
    private void DrawNode(int slot, int x, int y, int mapRow)
    {
        var node = world.Map.ResourceNodes[slot];
        (int workMode, int kind) = slot >= 1 && slot <= world.BuildingSlots
            ? ((int)world.Building(slot).WorkMode, world.Building(slot).Kind)
            : (0, 0);
        int seen = slot >= 1 && slot < visibility.Length ? visibility[slot] : 0;
        DrawUpper(ObjectRequest.ResourceNode(node.Kind, node.Stage, slot, ObjectPass.Upper) with
        {
            Tick = presentation.Cycle,
            MapRow = mapRow,
            SlotWorkMode = workMode,
            SlotBuildingKind = kind,
            OccupantsVisible = slot < FirstComputerSlot || (seen & 3) >= 2,
        }, x, y);
    }

    /// <summary>The trap a unit stands in (the unit drawn caught in it), or an ordered trap.</summary>
    private void DrawTrapWithUnit(int trapCode, int unitId, int x, int y)
    {
        int mapRow = (y / cellHeight) + view.Row;
        if (CellClasses.OfCode(trapCode, sight) == CellClasses.PendingTrap)
        {
            DrawUpper(ObjectRequest.Trap(CellClasses.PendingTrapKind, 0, ObjectPass.Upper) with { Tick = presentation.Cycle, MapRow = mapRow }, x, y);
            return;
        }
        ObjectRequest trap = ObjectRequest.Trap(CellClasses.KindOfCode(trapCode), 2, ObjectPass.Upper) with
        {
            Tick = presentation.Cycle,
            MapRow = mapRow,
            MapSelectionMarks = !world.Script.Active,
        };
        DrawObject(trap, x, y, new[] { (0, unitId) });
    }

    /// <summary>An object without occupants. A missing picture stops the object only.</summary>
    private void DrawUpper(ObjectRequest objectRequest, int x, int y)
    {
        ObjectComposition composition = composers.Objects.Compose(objectRequest with { PaletteVariant = variant, MapRow = ClockRow(objectRequest.MapRow) });
        output.Add(composition.Drawing.Parts, x, y);
        variant = composition.Drawing.PaletteVariantAfter;
    }

    /// <summary>
    /// An object with its occupants (in group order); each occupant's overlays (thoughts, health
    /// bar, status effects) follow its body, before the object's later parts.
    /// </summary>
    private void DrawObject(ObjectRequest objectRequest, int x, int y, IReadOnlyList<(int Group, int Unit)> occupantUnits)
    {
        List<Occupant> occupants = occupantList;
        occupants.Clear();
        unitOfGroup.Clear();
        for (int i = 0; i < occupantUnits.Count; i++)
        {
            (int group, int id) = occupantUnits[i];
            Unit unit = world.Unit(id);
            occupants.Add(new Occupant(group, unit.ActionSubState, unit.Facing, unit.AnimationNumber, unit.AnimationTick,
                Appearance(unit, id)));
            unitOfGroup[group] = id;
        }
        ObjectComposition composition = composers.Objects.Compose(objectRequest with
        {
            PaletteVariant = variant,
            Occupants = occupants,
            MapRow = ClockRow(objectRequest.MapRow),
        });
        IReadOnlyList<DrawPart> parts = composition.Drawing.Parts;
        int done = 0;
        foreach (PlacedOccupant placed in composition.Occupants)
        {
            int id = unitOfGroup[placed.Group];
            Unit unit = world.Unit(id);
            BodyComposition body = composers.Bodies.Compose(OccupantBody(objectRequest, placed, unit, id));
            IReadOnlyList<DrawPart> bodyParts = body.Drawing.Parts;
            int start = FindRun(parts, done, bodyParts, placed.X, placed.Y);
            if (start < 0)
            {
                continue;
            }
            EmitRange(parts, done, start - done, x, y, -1, MoverKind.None, 0);
            EmitRange(parts, start, bodyParts.Count, x, y, unit.Tribe, MoverKind.None, 0);
            done = start + bodyParts.Count;
            if (!body.Drawing.Truncated && body.HeadAnchor is { } head && body.TorsoAnchor is { } torso)
            {
                int carried = variant;
                variant = body.Drawing.PaletteVariantAfter;
                DrawOverlaysSafely(id, x + placed.X + head.X, y + placed.Y + head.Y, x + placed.X + torso.X, y + placed.Y + torso.Y,
                    true, MoverKind.None);
                variant = carried;
            }
        }
        EmitRange(parts, done, parts.Count - done, x, y, -1, MoverKind.None, 0);
        variant = composition.Drawing.PaletteVariantAfter;
    }

    /// <summary>The body request the object composer makes for an occupant drawn at <paramref name="placed"/>.</summary>
    private BodyRequest OccupantBody(ObjectRequest objectRequest, PlacedOccupant placed, Unit unit, int id)
    {
        VillagerAppearance look = Appearance(unit, id);
        const int TrainingGround = 10, Tower = 11;
        bool insidePose = unit.ActionSubState <= 0
            && (look.CarriedItem != 0 || look.Weapon == VillagerAppearance.Stone)
            && objectRequest.RuleKind != TrainingGround && objectRequest.RuleKind != Tower;
        return new BodyRequest(BodySubject.Villager, unit.AnimationNumber, placed.Facing, unit.AnimationTick)
        {
            Appearance = look,
            InsideBuildingPose = insidePose,
            LungeFacing = unit.Facing,
            MapSelectionMarks = objectRequest.MapSelectionMarks,
        };
    }

    /// <summary>Where the parts <paramref name="body"/>, moved by (<paramref name="dx"/>, <paramref name="dy"/>), start in <paramref name="parts"/>.</summary>
    private static int FindRun(IReadOnlyList<DrawPart> parts, int from, IReadOnlyList<DrawPart> body, int dx, int dy)
    {
        for (int start = from; start + body.Count <= parts.Count; start++)
        {
            bool same = true;
            for (int j = 0; j < body.Count && same; j++)
            {
                DrawPart a = parts[start + j];
                DrawPart b = body[j];
                same = a.Kind == b.Kind && a.Piece == b.Piece && a.X == b.X + dx && a.Y == b.Y + dy
                    && a.Width == b.Width && a.Height == b.Height;
            }
            if (same)
            {
                return start;
            }
        }
        return -1;
    }

    /// <summary>Reads the viewer's resource counts once per composition (the store's piles and the kitchen's food show them).</summary>
    private void ReadViewerStock()
    {
        TribeState state = world.Tribes[sight.Home];
        if (viewerStock.Length != TribeState.CounterCount)
        {
            viewerStock = new int[TribeState.CounterCount];
        }
        for (int i = 0; i < viewerStock.Length; i++)
        {
            viewerStock[i] = state.Counter(i);
        }
    }
}
