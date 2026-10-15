using System;
using System.Collections.Generic;
using Tribes.Client.World.Scene;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;
using Tribes.Graphics.Composition;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>What a tap or click on the map hit.</summary>
public enum PickKind
{
    /// <summary>Outside the playable map.</summary>
    Outside,

    /// <summary>Open ground, or land the player has not explored.</summary>
    Ground,

    /// <summary>One of the player's units.</summary>
    OwnUnit,

    /// <summary>A unit of another tribe.</summary>
    OtherUnit,

    /// <summary>A wild animal.</summary>
    Animal,

    /// <summary>One of the player's buildings (sites included).</summary>
    OwnBuilding,

    /// <summary>A building of another tribe.</summary>
    OtherBuilding,

    /// <summary>A resource node (trees, berries, stones...).</summary>
    Node,

    /// <summary>A trap, a spotted trap or an ordered one.</summary>
    Trap,

    /// <summary>Thicket, rocks, shore or other terrain nobody stands on.</summary>
    Terrain,
}

/// <summary>A pick: what was hit, on which cell, its slot and the cell code an order aims at.</summary>
/// <param name="Kind">What was hit.</param>
/// <param name="Cell">The map cell (of the creature, or under the pointer).</param>
/// <param name="Id">Unit slot, animal slot, building slot or node slot; 0 otherwise.</param>
/// <param name="TargetCode">The cell code (<see cref="CellCode"/>) a MoveUnits order aims at.</param>
public readonly record struct MapPick(PickKind Kind, GridPoint Cell, int Id, int TargetCode)
{
    /// <summary>Whether the pick is a creature (unit or animal).</summary>
    public bool IsCreature => Kind is PickKind.OwnUnit or PickKind.OtherUnit or PickKind.Animal;
}

/// <summary>The screen box of a unit or animal as the object layer drew it, in map pixels.</summary>
/// <param name="Mover">Unit or animal.</param>
/// <param name="Id">Its slot.</param>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
/// <param name="Order">The drawing order of its last part (higher is drawn on top).</param>
public readonly record struct CreatureBox(MoverKind Mover, int Id, int X, int Y, int Width, int Height, int Order)
{
    /// <summary>Whether map point (<paramref name="px"/>, <paramref name="py"/>) is inside, grown by <paramref name="grow"/> pixels.</summary>
    public bool Contains(float px, float py, float grow = 0) =>
        px >= X - grow && px < X + Width + grow && py >= Y - grow && py < Y + Height + grow;

    /// <summary>Squared distance from the box's centre to a point.</summary>
    public float DistanceSquared(float px, float py)
    {
        float dx = X + (Width / 2f) - px, dy = Y + (Height / 2f) - py;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>Centre of the box.</summary>
    public (float X, float Y) Centre => (X + (Width / 2f), Y + (Height / 2f));
}

/// <summary>
/// Finds what is under a point of the map: the units and animals as the object layer drew them
/// (their real picture, so a tap on a villager's head picks it), else what fills the cell.
/// Nothing the player cannot see is picked: creatures are only drawn where the player sees them,
/// and buildings, nodes and traps count only on land the player has explored.
/// </summary>
public static class MapPicker
{
    /// <summary>
    /// The boxes of the units and animals in <paramref name="items"/>, composed for <paramref name="view"/>,
    /// moved by <paramref name="drawnOffset"/> (where smooth motion draws each one now), if given.
    /// </summary>
    public static List<CreatureBox> Boxes(IReadOnlyList<SceneItem> items, ViewRect view, int cellWidth, int cellHeight,
        Func<MoverKind, int, (int X, int Y)>? drawnOffset = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var boxes = new Dictionary<(MoverKind, int), (int X0, int Y0, int X1, int Y1, int Order)>();
        int ox = view.Column * cellWidth, oy = view.Row * cellHeight;
        for (int i = 0; i < items.Count; i++)
        {
            SceneItem item = items[i];
            if (item.Mover is not (MoverKind.Unit or MoverKind.Animal) || item.Part.Kind != DrawPartKind.Image)
            {
                continue;
            }
            DrawPart p = item.Part;
            int x0 = ox + p.X, y0 = oy + p.Y, x1 = x0 + p.Width, y1 = y0 + p.Height;
            var key = (item.Mover, item.MoverId);
            boxes[key] = boxes.TryGetValue(key, out var b)
                ? (Math.Min(b.X0, x0), Math.Min(b.Y0, y0), Math.Max(b.X1, x1), Math.Max(b.Y1, y1), i)
                : (x0, y0, x1, y1, i);
        }
        var result = new List<CreatureBox>(boxes.Count);
        foreach (var ((mover, id), b) in boxes)
        {
            (int dx, int dy) = drawnOffset?.Invoke(mover, id) ?? (0, 0);
            result.Add(new CreatureBox(mover, id, b.X0 + dx, b.Y0 + dy, b.X1 - b.X0, b.Y1 - b.Y0, b.Order));
        }
        return result;
    }

    /// <summary>
    /// What is at map point (<paramref name="px"/>, <paramref name="py"/>) for <paramref name="tribe"/>:
    /// a creature whose picture covers the point (the topmost), else a building on that cell or
    /// whose picture covers the point (with <paramref name="shapes"/>; see <see cref="BuildingAt"/>),
    /// else the creature nearest the point within <paramref name="tolerance"/> pixels, else the
    /// cell's content as <paramref name="sight"/> knows it (the tribe's own fog when null).
    /// </summary>
    public static MapPick Pick(GameWorld world, int tribe, IReadOnlyList<CreatureBox> boxes, float px, float py,
        float tolerance, int cellWidth, int cellHeight, IFogView? sight = null, BuildingShapes? shapes = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(boxes);
        if (BestCreature(world, boxes, px, py, 0) is { } creature)
        {
            return ForCreature(world, tribe, creature);
        }
        var cell = new GridPoint((int)Math.Floor(px / cellWidth), (int)Math.Floor(py / cellHeight));
        MapPick atCell = AtCell(world, tribe, cell, sight);
        if (shapes is not null)
        {
            // The building drawn in front there, also when another building's footprint holds the cell.
            if (BuildingAt(world, tribe, shapes, px, py, cellWidth, cellHeight, sight) is { } building)
            {
                return building;
            }
        }
        else if (atCell.Kind is PickKind.OwnBuilding or PickKind.OtherBuilding)
        {
            return atCell;
        }
        // A tap just beside a villager still takes it (a finger is wider than a villager).
        return tolerance > 0 && BestCreature(world, boxes, px, py, tolerance) is { } near ? ForCreature(world, tribe, near) : atCell;
    }

    /// <summary>
    /// What fills <paramref name="cell"/> for <paramref name="tribe"/> (creatures not counted), as
    /// <paramref name="sight"/> knows it (the tribe's own fog when null).
    /// </summary>
    public static MapPick AtCell(GameWorld world, int tribe, GridPoint cell, IFogView? sight = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.Map.InPlay(cell))
        {
            return new MapPick(PickKind.Outside, cell, 0, CellCode.Impassable);
        }
        IFogView fog = sight ?? world.Fog(tribe);
        MapCell content = world.Map.CellAt(cell);
        int code = CellCode.ToCode(content);
        if (!fog.IsExplored(cell))
        {
            return new MapPick(PickKind.Ground, cell, 0, CellCode.Empty);
        }
        if (content.Content == CellContent.Building && OutOfSight(world, tribe, fog, world.Building(content.Index)))
        {
            // Only its remembered look is on the map: nothing live to show or aim at there.
            return new MapPick(PickKind.Ground, cell, 0, CellCode.Empty);
        }
        return content.Content switch
        {
            CellContent.Building => new MapPick(world.Building(content.Index).Tribe == tribe ? PickKind.OwnBuilding : PickKind.OtherBuilding,
                cell, content.Index, code),
            CellContent.ResourceNode => new MapPick(PickKind.Node, cell, content.Index, code),
            CellContent.Trap or CellContent.SpottedTrap or CellContent.PendingTrap => new MapPick(PickKind.Trap, cell, 0, code),
            CellContent.Thicket or CellContent.Rocks or CellContent.Shore or CellContent.Impassable => new MapPick(PickKind.Terrain, cell, 0, code),
            // Units and animals are picked by their pictures; a cell they stand on counts as ground.
            _ => new MapPick(PickKind.Ground, cell, 0, CellCode.Empty),
        };
    }


    /// <summary>
    /// The building whose picture covers map point (<paramref name="px"/>, <paramref name="py"/>)
    /// for <paramref name="tribe"/>, if any, as <paramref name="sight"/> knows the land (the tribe's own
    /// fog when null): a roof, a tower's platform with its guard and the other parts drawn above or
    /// beside its footprint count as the building. Of two pictures the one drawn in front wins (the
    /// lower footprint; in the same row the one further left). The pick's cell is the footprint cell
    /// nearest the point, where an order aims.
    /// </summary>
    public static MapPick? BuildingAt(GameWorld world, int tribe, BuildingShapes shapes, float px, float py,
        int cellWidth, int cellHeight, IFogView? sight = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(shapes);
        IFogView fog = sight ?? world.Fog(tribe);
        int cx = (int)Math.Floor(px / cellWidth), cy = (int)Math.Floor(py / cellHeight);
        Building? best = null;
        int bestBottom = int.MinValue;
        // A picture reaches at most a few rows above its footprint and a cell beside it, so its
        // footprint lies in this window around the point.
        for (int y = cy - 1; y <= cy + BuildingShapes.RowsAbove; y++)
        {
            for (int x = cx - BuildingShapes.CellsBeside; x <= cx + BuildingShapes.CellsBeside; x++)
            {
                var cell = new GridPoint(x, y);
                if (!world.Map.InPlay(cell) || world.Map.CellAt(cell) is not { Content: CellContent.Building } content
                    || !fog.IsExplored(cell))
                {
                    continue;
                }
                Building building = world.Building(content.Index);
                if (!building.Stands || building == best || OutOfSight(world, tribe, fog, building)
                    || !shapes.Of(world, building, cellWidth, cellHeight).Contains(px, py))
                {
                    continue;
                }
                (_, int height) = FootprintCheck.Size(world, Math.Clamp(building.Kind, 0, world.Content.Rules.Buildings.Kinds.Count - 1));
                int bottom = building.Y + height;
                if (best is null || bottom > bestBottom || (bottom == bestBottom && building.X < best.X))
                {
                    best = building;
                    bestBottom = bottom;
                }
            }
        }
        if (best is null)
        {
            return null;
        }
        (int width, int footprintHeight) = FootprintCheck.Size(world, Math.Clamp(best.Kind, 0, world.Content.Rules.Buildings.Kinds.Count - 1));
        var aim = new GridPoint(Math.Clamp(cx, best.X, best.X + width - 1), Math.Clamp(cy, best.Y, best.Y + footprintHeight - 1));
        return new MapPick(best.Tribe == tribe ? PickKind.OwnBuilding : PickKind.OtherBuilding, aim, best.Id, CellCode.OfBuilding(best.Id));
    }

    /// <summary>
    /// Whether <paramref name="building"/> of another tribe is out of sight in a match: the map then
    /// shows it only as last seen, so it is not picked (its live state is not the player's to see).
    /// </summary>
    public static bool OutOfSight(GameWorld world, int tribe, IFogView fog, Building building)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(fog);
        ArgumentNullException.ThrowIfNull(building);
        if (world.Mode != WorldMode.Match || building.Tribe == tribe)
        {
            return false;
        }
        (int width, int height) = FootprintCheck.Size(world, Math.Clamp(building.Kind, 0, world.Content.Rules.Buildings.Kinds.Count - 1));
        return !fog.AnyVisible(new GridRect(building.X, building.Y, width, height));
    }

    /// <summary>
    /// Whether the thing of another tribe with cell code <paramref name="code"/>, shown in the panel,
    /// is still in sight for <paramref name="tribe"/>: in a match a building out of sight shows only
    /// as last seen and a villager or animal out of sight is not drawn, so their live state is no
    /// longer shown. In the campaign whatever was picked stays shown, as in the original.
    /// </summary>
    public static bool StillInSight(GameWorld world, int tribe, IFogView fog, int code)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(fog);
        if (world.Mode != WorldMode.Match)
        {
            return true;
        }
        if (CellTargets.BuildingOf(world, code) is int building)
        {
            return !OutOfSight(world, tribe, fog, world.Building(building));
        }
        if (CellTargets.UnitOf(world, code) is int unit)
        {
            Unit u = world.Unit(unit);
            return u.Tribe == tribe || fog.IsVisible(new GridPoint(u.X, u.Y));
        }
        if (CellTargets.AnimalOf(world, code) is int animal)
        {
            Animal a = world.Animal(animal);
            return fog.IsVisible(new GridPoint(a.X, a.Y));
        }
        return true;
    }

    /// <summary>The player's units whose pictures have their centre inside the map rectangle.</summary>
    public static List<int> UnitsInBox(GameWorld world, int tribe, IReadOnlyList<CreatureBox> boxes,
        float x0, float y0, float x1, float y1)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(boxes);
        float left = Math.Min(x0, x1), right = Math.Max(x0, x1), top = Math.Min(y0, y1), bottom = Math.Max(y0, y1);
        var picked = new List<(int Id, float X, float Y)>();
        foreach (CreatureBox box in boxes)
        {
            if (box.Mover != MoverKind.Unit || !IsOwnLivingUnit(world, tribe, box.Id))
            {
                continue;
            }
            (float cx, float cy) = box.Centre;
            if (cx >= left && cx <= right && cy >= top && cy <= bottom)
            {
                picked.Add((box.Id, cx, cy));
            }
        }
        // Reading order (top to bottom, left to right): the order the rules go through them.
        picked.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        return picked.ConvertAll(p => p.Id);
    }

    /// <summary>Whether unit slot <paramref name="unit"/> is a living unit of <paramref name="tribe"/>.</summary>
    public static bool IsOwnLivingUnit(GameWorld world, int tribe, int unit)
    {
        ArgumentNullException.ThrowIfNull(world);
        return unit >= 1 && unit <= world.UnitSlots && world.Unit(unit).Tribe == tribe && world.Unit(unit).HitPoints > 0;
    }

    private static CreatureBox? BestCreature(GameWorld world, IReadOnlyList<CreatureBox> boxes, float px, float py, float tolerance)
    {
        CreatureBox? best = null;
        foreach (CreatureBox box in boxes)
        {
            if (!IsAlive(world, box) || !box.Contains(px, py))
            {
                continue;
            }
            if (best is null || box.Order > best.Value.Order)
            {
                best = box;
            }
        }
        if (best is not null || tolerance <= 0)
        {
            return best;
        }
        float bestDistance = float.MaxValue;
        foreach (CreatureBox box in boxes)
        {
            if (!IsAlive(world, box) || !box.Contains(px, py, tolerance))
            {
                continue;
            }
            float distance = box.DistanceSquared(px, py);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = box;
            }
        }
        return best;
    }

    private static bool IsAlive(GameWorld world, CreatureBox box) => box.Mover == MoverKind.Unit
        ? box.Id >= 1 && box.Id <= world.UnitSlots && world.Unit(box.Id).HitPoints > 0
        : box.Id >= 0 && box.Id < world.AnimalSlots && world.Animal(box.Id).HitPoints > 0;

    private static MapPick ForCreature(GameWorld world, int tribe, CreatureBox box)
    {
        if (box.Mover == MoverKind.Animal)
        {
            Animal animal = world.Animal(box.Id);
            return new MapPick(PickKind.Animal, new GridPoint(animal.X, animal.Y), box.Id, CellCode.OfAnimal(box.Id));
        }
        Unit unit = world.Unit(box.Id);
        return new MapPick(unit.Tribe == tribe ? PickKind.OwnUnit : PickKind.OtherUnit, new GridPoint(unit.X, unit.Y), box.Id, CellCode.OfUnit(box.Id));
    }
}
