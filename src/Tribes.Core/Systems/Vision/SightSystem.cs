using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Vision;

/// <summary>Which of a tribe's fog layers a reveal writes.</summary>
internal enum FogLayer
{
    /// <summary>What was ever seen.</summary>
    Explored,

    /// <summary>What is seen now.</summary>
    Visible,
}

/// <summary>
/// The fog of war of the human-ruled tribes (the original's player): what its units and buildings
/// see, what enemies give away by fighting, and buildings that become known as a whole once any
/// of their cells is explored. Computer tribes have no fog. In a match allies share vision: a
/// tribe also sees what its allies' units and buildings see, and allied human-ruled tribes share
/// the land they have explored, so every rule that reads a tribe's fog (orders, targets,
/// placement, routes) counts what its allies show it.
/// </summary>
internal sealed class SightSystem
{
    /// <summary>The <see cref="Building.Visibility"/> value of a building whose footprint has been revealed.</summary>
    public const int RevealedVisibility = 4;

    /// <summary>Enemy units playing an attack animation in this range are seen where they stand.</summary>
    private const int FirstAttackAnimation = 23, LastAttackAnimation = 36;

    private readonly GameWorld world;
    private readonly Passability passability;
    private readonly SightSharing sharing;

    /// <summary>
    /// For each human-ruled tribe of a match, the buildings whose whole footprint it has explored,
    /// by slot (the building's place and kind, to tell a new building in the slot apart). Explored
    /// cells stay explored, so such a building never needs revealing again: only work is saved.
    /// </summary>
    private readonly Dictionary<int, long[]> fullyExplored = new();

    /// <summary>Copies of the human-ruled tribes' own sight while allies share it (memory only).</summary>
    private readonly Dictionary<int, VisibleCopy> sightCopies = new();

    /// <summary>The slots a tribe remembers, listed to look them over (memory only).</summary>
    private readonly List<int> rememberedSlots = new();

    /// <summary>Works on the fog of <paramref name="world"/>'s human-ruled tribes.</summary>
    public SightSystem(GameWorld world, Passability passability)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.passability = passability ?? throw new ArgumentNullException(nameof(passability));
        sharing = new SightSharing(world);
    }

    /// <summary>
    /// Recomputes what every human-ruled tribe sees now (its units' sight discs, the cells of its
    /// enemies attacking, its buildings' sight or, for sites, their footprint) and adds it to what it
    /// has explored. In a match a tribe also sees what its allies' units and buildings see (an ally's
    /// own sight, not what the ally sees through its other allies), and allied human-ruled tribes
    /// then share what they have explored (see <see cref="ShareExploredLand"/>).
    /// </summary>
    public void UpdateSight()
    {
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Controller.IsHumanRuled)
            {
                RevealOwnSight(tribe.Index);
            }
        }
        if (world.Mode == WorldMode.Match)
        {
            ShareSight();
        }
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Controller.IsHumanRuled)
            {
                world.Fog(tribe.Index).ExploreVisible();
            }
        }
        if (world.Mode == WorldMode.Match)
        {
            RememberSeenBuildings();
        }
        ShareExploredLand();
    }

    /// <summary>
    /// The test reveal of a single-player game: <paramref name="tribe"/> has explored every cell of
    /// <paramref name="area"/> (land not made yet is skipped) and, in a match, remembers every
    /// building with its origin there as it looks now.
    /// </summary>
    public void ExploreForTest(int tribe, GridRect area)
    {
        FogOfWar fog = world.Fog(tribe);
        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++)
            {
                var cell = new GridPoint(x, y);
                if (!world.Map.IsChunked || world.Map.Chunk(ChunkCoord.Of(cell)) != null)
                {
                    fog.SetExplored(cell, true);
                }
            }
        }
        if (world.Mode != WorldMode.Match)
        {
            return;
        }
        for (int id = 1; id <= world.BuildingSlots; id++)
        {
            Building building = world.Building(id);
            if (building.InUse && building.X >= area.Left && building.X < area.Right && building.Y >= area.Top && building.Y < area.Bottom)
            {
                fog.Buildings.Remember(RememberedBuilding.Of(building, world.Frame));
            }
        }
    }

    /// <summary>
    /// In a match, every human-ruled tribe remembers how each building it sees now looks, and
    /// forgets one whose place it sees without it (destroyed, or another building there now).
    /// </summary>
    private void RememberSeenBuildings()
    {
        var kinds = world.Content.Rules.Buildings.Kinds;
        foreach (TribeState tribe in world.Tribes)
        {
            if (!tribe.Controller.IsHumanRuled)
            {
                continue;
            }
            FogOfWar fog = world.Fog(tribe.Index);
            BuildingMemory memory = fog.Buildings;
            for (int id = 1; id <= world.BuildingSlots; id++)
            {
                Building building = world.Building(id);
                if (building.InUse && fog.AnyVisible(new GridRect(building.X, building.Y, kinds[building.Kind].Width, kinds[building.Kind].Height)))
                {
                    memory.Remember(RememberedBuilding.Of(building, world.Frame));
                }
            }
            memory.SlotsInto(rememberedSlots);
            foreach (int slot in rememberedSlots)
            {
                memory.TryGet(slot, out RememberedBuilding known);
                if (!fog.AnyVisible(known.Footprint(world.Content)))
                {
                    continue;
                }
                Building now = world.Building(slot);
                if (!now.InUse || now.X != known.X || now.Y != known.Y)
                {
                    memory.Forget(slot);
                }
            }
        }
    }

    /// <summary>
    /// In a match, adds to each human-ruled tribe's explored land what its human-ruled allies have
    /// explored, and to the buildings it remembers those they saw later. Explored land and the
    /// buildings remembered stay when an alliance ends; only the shared sight stops.
    /// </summary>
    public void ShareExploredLand()
    {
        foreach (TribeState tribe in world.Tribes)
        {
            if (!tribe.Controller.IsHumanRuled)
            {
                continue;
            }
            foreach (int ally in sharing.AlliesOf(tribe.Index))
            {
                if (world.Tribes[ally].Controller.IsHumanRuled)
                {
                    world.Fog(tribe.Index).AddExplored(world.Fog(ally));
                    world.Fog(tribe.Index).Buildings.AddFrom(world.Fog(ally).Buildings);
                }
            }
        }
    }

    /// <summary>
    /// Forgets what <paramref name="tribe"/> saw and reveals what it sees itself now. An enemy
    /// fighting gives away where it stands: in the campaign every enemy that strikes (the original's
    /// one enemy can only strike the player); in a match one that strikes the tribe or a computer
    /// ally of it (a human-ruled ally's attackers come with that ally's sight), not a fight between
    /// other tribes.
    /// </summary>
    private void RevealOwnSight(int tribe)
    {
        FogOfWar fog = world.Fog(tribe);
        fog.ClearVisible();
        RevealUnitsSight(fog, tribe);
        bool anyFight = world.Mode == WorldMode.Campaign;
        foreach (int enemy in TribeRoles.EnemiesOf(world, tribe))
        {
            foreach (Unit unit in world.UnitsOf(enemy))
            {
                if (unit.InUse && GivesAwayPosition(unit) && (anyFight || StrikesAtSharer(unit, tribe)))
                {
                    RevealDisc(fog, FogLayer.Visible, unit.X, unit.Y, 0);
                }
            }
        }
        RevealBuildingsSight(fog, tribe);
    }

    /// <summary>Whether the target of <paramref name="attacker"/> is <paramref name="tribe"/>'s, or a computer ally's of it.</summary>
    private bool StrikesAtSharer(Unit attacker, int tribe)
    {
        int victim = TribeOfTarget(attacker.TargetCellCode);
        if (victim == tribe)
        {
            return true;
        }
        if (victim < 0 || world.Tribes[victim].Controller.IsHumanRuled)
        {
            return false;
        }
        foreach (int ally in sharing.AlliesOf(tribe))
        {
            if (ally == victim)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The tribe whose unit or building <paramref name="code"/> is; -1 for anything else.</summary>
    private int TribeOfTarget(int code)
    {
        int unit = CellCode.UnitOf(code);
        if (unit > 0 && unit <= world.UnitSlots)
        {
            return world.TribeOfUnit(unit);
        }
        int building = CellCode.BuildingOf(code);
        return building > 0 && building <= world.BuildingSlots ? world.TribeOfBuilding(building) : -1;
    }

    /// <summary>
    /// Adds to each human-ruled tribe's sight its allies' own sight: a human-ruled ally's is copied
    /// from its fog before anything is added to it (so nothing is passed on through a chain of
    /// allies, and each tribe's sight is worked out once however many allies share it); a computer
    /// ally, which keeps no fog, has its units' and buildings' sight revealed for the tribe.
    /// </summary>
    private void ShareSight()
    {
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Controller.IsHumanRuled && HasHumanAlly(tribe.Index))
            {
                world.Fog(tribe.Index).CopyVisible(CopyOf(tribe.Index));
            }
        }
        foreach (TribeState tribe in world.Tribes)
        {
            if (!tribe.Controller.IsHumanRuled)
            {
                continue;
            }
            FogOfWar fog = world.Fog(tribe.Index);
            foreach (int ally in sharing.AlliesOf(tribe.Index))
            {
                if (world.Tribes[ally].Controller.IsHumanRuled)
                {
                    fog.AddVisible(CopyOf(ally));
                }
                else
                {
                    RevealUnitsSight(fog, ally);
                    RevealBuildingsSight(fog, ally);
                }
            }
        }
    }

    private bool HasHumanAlly(int tribe)
    {
        foreach (int ally in sharing.AlliesOf(tribe))
        {
            if (world.Tribes[ally].Controller.IsHumanRuled)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The copy of <paramref name="tribe"/>'s own sight (kept from one update to the next to reuse its memory).</summary>
    private VisibleCopy CopyOf(int tribe)
    {
        if (!sightCopies.TryGetValue(tribe, out VisibleCopy? copy))
        {
            copy = new VisibleCopy();
            sightCopies[tribe] = copy;
        }
        return copy;
    }

    /// <summary>The sight discs of <paramref name="tribe"/>'s units, revealed in <paramref name="fog"/>.</summary>
    private void RevealUnitsSight(FogOfWar fog, int tribe)
    {
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            if (unit.InUse)
            {
                RevealDisc(fog, FogLayer.Visible, unit.X, unit.Y, unit.SightRadius);
            }
        }
    }

    /// <summary>The sight of <paramref name="tribe"/>'s buildings (a site's footprint), revealed in <paramref name="fog"/>.</summary>
    private void RevealBuildingsSight(FogOfWar fog, int tribe)
    {
        foreach (Building building in world.BuildingsOf(tribe))
        {
            RevealAround(fog, building);
        }
    }

    /// <summary>
    /// Marks the whole footprint of every building explored once any of its cells in play is
    /// explored: in the campaign once per building, for the player; in a match for every
    /// human-ruled tribe, each time any of its cells is explored and the rest is not.
    /// </summary>
    public void RevealSeenBuildings()
    {
        for (int id = 1; id <= world.BuildingSlots; id++)
        {
            Building building = world.Building(id);
            if (!building.InUse)
            {
                continue;
            }
            BuildingKind kind = world.Content.Rules.Buildings.Kinds[building.Kind];
            if (world.Mode == WorldMode.Campaign)
            {
                RevealOnce(building, kind, TribeRoles.Player(world));
                continue;
            }
            foreach (TribeState tribe in world.Tribes)
            {
                if (!tribe.Controller.IsHumanRuled || KnownWhole(tribe.Index, building))
                {
                    continue;
                }
                (bool any, bool every) = Explored(building, kind, tribe.Index);
                if (any && !every)
                {
                    RevealRect(tribe.Index, FogLayer.Explored, building.X, building.Y, kind.Width, kind.Height);
                    building.Visibility = RevealedVisibility;
                    every = AnyCellExplored(building, kind, tribe.Index, all: true);
                }
                if (every)
                {
                    RememberWhole(tribe.Index, building);
                }
            }
        }
    }

    /// <summary>Reveals the disc of <paramref name="radius"/> around (<paramref name="x"/>, <paramref name="y"/>) to the original's player.</summary>
    public void RevealDisc(FogLayer layer, int x, int y, int radius) => RevealDisc(TribeRoles.Player(world), layer, x, y, radius);

    /// <summary>Reveals a rectangle to the original's player (see <see cref="RevealRect(int, FogLayer, int, int, int, int)"/>).</summary>
    public void RevealRect(FogLayer layer, int x, int y, int width, int height) =>
        RevealRect(TribeRoles.Player(world), layer, x, y, width, height);

    /// <summary>
    /// Reveals the disc of <paramref name="radius"/> around (<paramref name="x"/>, <paramref name="y"/>)
    /// to <paramref name="tribe"/>; what it sees now is seen by its human-ruled allies too (in a match).
    /// </summary>
    public void RevealDisc(int tribe, FogLayer layer, int x, int y, int radius)
    {
        RevealDisc(world.Fog(tribe), layer, x, y, radius);
        if (layer == FogLayer.Visible)
        {
            foreach (int ally in sharing.AlliesOf(tribe))
            {
                if (world.Tribes[ally].Controller.IsHumanRuled)
                {
                    RevealDisc(world.Fog(ally), layer, x, y, radius);
                }
            }
        }
    }

    /// <summary>
    /// Reveals the rectangle of <paramref name="width"/> by <paramref name="height"/> cells from
    /// (<paramref name="x"/>, <paramref name="y"/>) to <paramref name="tribe"/>; what it sees now is
    /// seen by its human-ruled allies too (in a match).
    /// </summary>
    public void RevealRect(int tribe, FogLayer layer, int x, int y, int width, int height)
    {
        RevealRect(world.Fog(tribe), layer, x, y, width, height);
        if (layer == FogLayer.Visible)
        {
            foreach (int ally in sharing.AlliesOf(tribe))
            {
                if (world.Tribes[ally].Controller.IsHumanRuled)
                {
                    RevealRect(world.Fog(ally), layer, x, y, width, height);
                }
            }
        }
    }

    private void RevealDisc(FogOfWar fog, FogLayer layer, int x, int y, int radius)
    {
        RevealDisc disc = world.Content.Rules.Movement.RevealDiscs[radius];
        for (int i = 0; i < disc.RowWidths.Count; i++)
        {
            int row = y - radius + i;
            int width = disc.RowWidths[i];
            RevealRow(fog, layer, x - (width >> 1), row, width);
        }
    }

    private void RevealRect(FogOfWar fog, FogLayer layer, int x, int y, int width, int height)
    {
        for (int row = y; row < y + height; row++)
        {
            RevealRow(fog, layer, x, row, width);
        }
    }

    /// <summary>A building's place and kind as one number (its slot may later hold another building).</summary>
    private static long Footprint(Building building) =>
        ((long)building.X << 40) ^ ((long)(building.Y & 0xFFFFF) << 16) ^ (building.Kind & 0xFFFF) ^ (1L << 62);

    private bool KnownWhole(int tribe, Building building) =>
        fullyExplored.TryGetValue(tribe, out long[]? known) && building.Id < known.Length && known[building.Id] == Footprint(building);

    private void RememberWhole(int tribe, Building building)
    {
        if (!fullyExplored.TryGetValue(tribe, out long[]? known) || known.Length <= building.Id)
        {
            long[] grown = new long[world.BuildingSlots + 1];
            known?.CopyTo(grown, 0);
            fullyExplored[tribe] = known = grown;
        }
        known[building.Id] = Footprint(building);
    }

    private void RevealOnce(Building building, BuildingKind kind, int tribe)
    {
        if ((building.Visibility & RevealedVisibility) == 0 && AnyCellExplored(building, kind, tribe, all: false))
        {
            RevealRect(tribe, FogLayer.Explored, building.X, building.Y, kind.Width, kind.Height);
            building.Visibility = RevealedVisibility;
        }
    }

    private void RevealAround(FogOfWar fog, Building building)
    {
        if (!building.InUse)
        {
            return;
        }
        BuildingKind kind = world.Content.Rules.Buildings.Kinds[building.Kind];
        if (building.Kind >= BuildingKindNumbers.PlannedKinds)
        {
            RevealRect(fog, FogLayer.Visible, building.X, building.Y, kind.Width, kind.Height);
        }
        else
        {
            RevealDisc(fog, FogLayer.Visible, building.X + (kind.Width / 2), building.Y + (kind.Height / 2), building.SightRadius);
        }
    }

    private static bool GivesAwayPosition(Unit unit) =>
        unit.Animation >= FirstAttackAnimation && unit.Animation <= LastAttackAnimation
        && unit.Action <= ActionNumbers.LastBasicAttack
        && unit.Action != ActionNumbers.HuntMelee && unit.Action != ActionNumbers.HuntRanged;

    /// <summary>
    /// Whether any cell of the footprint in play is explored by <paramref name="tribe"/>, and whether
    /// every one is (as <see cref="AnyCellExplored"/> without and with <c>all</c>, in one pass).
    /// </summary>
    private (bool Any, bool Every) Explored(Building building, BuildingKind kind, int tribe)
    {
        FogOfWar fog = world.Fog(tribe);
        bool any = false, every = true;
        for (int row = building.Y; row < building.Y + kind.Height; row++)
        {
            for (int column = building.X; column < building.X + kind.Width; column++)
            {
                var cell = new GridPoint(column, row);
                if (!world.Map.InPlay(cell))
                {
                    continue;
                }
                if (fog.IsExplored(cell))
                {
                    any = true;
                }
                else
                {
                    every = false;
                }
                if (any && !every)
                {
                    return (true, false);
                }
            }
        }
        return (any, every);
    }

    /// <summary>Whether any (or, with <paramref name="all"/>, every) cell of the footprint in play is explored by <paramref name="tribe"/>.</summary>
    private bool AnyCellExplored(Building building, BuildingKind kind, int tribe, bool all)
    {
        for (int row = building.Y; row < building.Y + kind.Height; row++)
        {
            for (int column = building.X; column < building.X + kind.Width; column++)
            {
                var cell = new GridPoint(column, row);
                if (!world.Map.InPlay(cell))
                {
                    continue;
                }
                bool explored = passability.FogAt(cell, tribe) != FogState.Unexplored;
                if (explored != all)
                {
                    return explored;
                }
            }
        }
        return all;
    }

    /// <summary>Reveals cells of one row in play; the fog covers whole rows of the largest map, even beyond the part in play.</summary>
    private void RevealRow(FogOfWar fog, FogLayer layer, int x, int row, int width)
    {
        GridRect bounds = world.Map.Bounds;
        if (row < bounds.Top || row >= bounds.Bottom)
        {
            return;
        }
        for (int column = Math.Max(x, bounds.Left); column < x + width; column++)
        {
            var cell = new GridPoint(column, row);
            if (layer == FogLayer.Visible)
            {
                fog.SetVisible(cell, true);
            }
            else
            {
                fog.SetExplored(cell, true);
            }
        }
    }
}
