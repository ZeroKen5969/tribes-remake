using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.World.Scene;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// A tap anywhere on a building's picture picks the building: a tower's platform with its guard,
/// a roof above the footprint, not only the cells of the footprint. A villager drawn in front of a
/// building is picked first; a second tap on it (shown already) reaches the building behind.
/// </summary>
public sealed class BuildingPickTests
{
    private const int CellWidth = 22, CellHeight = 16;

    /// <summary>The custom map whose computer tribe starts with manned towers, huts and the other buildings.</summary>
    private const int TowerMap = 7;

    private static readonly BuildingShapes Shapes = new(Repo.Instance.Assets.Composers.Objects);

    /// <summary>Every cell explored and seen.</summary>
    private sealed class AllSeen : IFogView
    {
        public bool IsExplored(GridPoint cell) => true;

        public bool IsVisible(GridPoint cell) => true;

        public GridRect ExploredBounds => new(0, 0, 1000, 1000);

        public bool AnyVisible(GridRect area) => !area.IsEmpty;

        public BuildingMemory Buildings { get; } = new();
    }

    /// <summary>Nothing explored.</summary>
    private sealed class NothingSeen : IFogView
    {
        public bool IsExplored(GridPoint cell) => false;

        public bool IsVisible(GridPoint cell) => false;

        public GridRect ExploredBounds => default;

        public bool AnyVisible(GridRect area) => false;

        public BuildingMemory Buildings { get; } = new();
    }

    /// <summary>Every cell explored, none seen now.</summary>
    private sealed class ExploredOnly : IFogView
    {
        public bool IsExplored(GridPoint cell) => true;

        public bool IsVisible(GridPoint cell) => false;

        public GridRect ExploredBounds => new(-1000, -1000, 2000, 2000);

        public bool AnyVisible(GridRect area) => false;

        public BuildingMemory Buildings { get; } = new();
    }

    [Fact]
    public void In_a_match_another_tribes_building_out_of_sight_is_not_picked()
    {
        GameWorld world = GameSimulation.StartEndless(Repo.Instance.Content, new Tribes.Core.Systems.Endless.EndlessSetup
        {
            Seed = 21,
            Tribes = Enumerable.Range(0, 2).Select(h => new Tribes.Core.Systems.Match.MatchTribe(TribeController.Human(h))).ToArray(),
            ComputerTribes = 0,
            NewTribes = 0,
        }).World;
        Assert.Equal(WorldMode.Match, world.Mode);
        Building theirs = BuildingOffers.StandingBuildings(world, 1).Select(world.Building).First();
        float x = (theirs.X * CellWidth) + 5, y = (theirs.Y * CellHeight) + 5;

        // Out of sight only its remembered look is on the map: the tap is a tap on the ground.
        Assert.Equal(PickKind.Ground, MapPicker.Pick(world, 0, [], x, y, 0, CellWidth, CellHeight, new ExploredOnly(), Shapes).Kind);
        Assert.Equal(PickKind.Ground, MapPicker.AtCell(world, 0, new GridPoint(theirs.X, theirs.Y), new ExploredOnly()).Kind);
        Assert.Null(MapPicker.BuildingAt(world, 0, Shapes, x, y, CellWidth, CellHeight, new ExploredOnly()));
        // In sight, and for its own tribe, it is picked.
        Assert.Equal(PickKind.OtherBuilding, MapPicker.Pick(world, 0, [], x, y, 0, CellWidth, CellHeight, new AllSeen(), Shapes).Kind);
        Assert.Equal(PickKind.OwnBuilding, MapPicker.Pick(world, 1, [], x, y, 0, CellWidth, CellHeight, new ExploredOnly(), Shapes).Kind);
    }

    [Fact]
    public void In_the_campaign_an_explored_building_is_picked_as_the_original_shows_it()
    {
        GameWorld world = World();
        Assert.Equal(WorldMode.Campaign, world.Mode);
        Building tower = Manned(world, RuleNumbers.Tower);

        MapPick pick = MapPicker.AtCell(world, 0, new GridPoint(tower.X, tower.Y), new ExploredOnly());

        Assert.Equal(PickKind.OtherBuilding, pick.Kind);
    }

    [Fact]
    public void In_a_match_the_panel_lets_go_of_what_leaves_sight()
    {
        GameWorld world = GameSimulation.StartEndless(Repo.Instance.Content, new Tribes.Core.Systems.Endless.EndlessSetup
        {
            Seed = 21,
            Tribes = Enumerable.Range(0, 2).Select(h => new Tribes.Core.Systems.Match.MatchTribe(TribeController.Human(h))).ToArray(),
            ComputerTribes = 0,
            NewTribes = 0,
        }).World;
        int building = CellCode.OfBuilding(BuildingOffers.StandingBuildings(world, 1)[0]);
        int unit = CellCode.OfUnit(BuildingOffers.LivingUnits(world, 1)[0]);

        Assert.False(MapPicker.StillInSight(world, 0, new ExploredOnly(), building));
        Assert.False(MapPicker.StillInSight(world, 0, new ExploredOnly(), unit));
        Assert.True(MapPicker.StillInSight(world, 0, new AllSeen(), building));
        Assert.True(MapPicker.StillInSight(world, 0, new AllSeen(), unit));
        // A tribe always sees its own.
        Assert.True(MapPicker.StillInSight(world, 1, new ExploredOnly(), building));
        Assert.True(MapPicker.StillInSight(world, 1, new ExploredOnly(), unit));
    }

    [Fact]
    public void In_the_campaign_the_panel_keeps_what_was_picked()
    {
        GameWorld world = World();
        Building tower = Manned(world, RuleNumbers.Tower);

        Assert.True(MapPicker.StillInSight(world, 0, new ExploredOnly(), CellCode.OfBuilding(tower.Id)));
    }

    private static GameWorld World()
    {
        GameSimulation game = Repo.Instance.Started(TowerMap);
        for (int i = 0; i < 600; i++)
        {
            game.Step(TickInput.Idle);
        }
        return game.World;
    }

    private static Building Manned(GameWorld world, int kind) =>
        BuildingOffers.StandingBuildings(world, 1).Select(world.Building)
            .First(b => b.Kind == kind && BuildingOffers.Inside(world, b).Count > 0);

    /// <summary>A point of the building's picture above its footprint.</summary>
    private static (float X, float Y) AboveFootprint(GameWorld world, Building b)
    {
        PixelRect picture = Shapes.Of(world, b, CellWidth, CellHeight);
        Assert.True(picture.Y < b.Y * CellHeight, $"kind {b.Kind} has nothing drawn above its footprint");
        return (b.X * CellWidth + (FootprintCheck.Size(world, b.Kind).Width * CellWidth / 2f), picture.Y + ((b.Y * CellHeight) - picture.Y) / 2f);
    }

    [Fact]
    public void A_tap_on_a_manned_towers_platform_picks_the_tower()
    {
        GameWorld world = World();
        Building tower = Manned(world, RuleNumbers.Tower);
        (float x, float y) = AboveFootprint(world, tower);

        MapPick pick = MapPicker.Pick(world, 0, [], x, y, 0, CellWidth, CellHeight, new AllSeen(), Shapes);

        Assert.Equal(PickKind.OtherBuilding, pick.Kind);
        Assert.Equal(tower.Id, pick.Id);
        Assert.Equal(CellCode.OfBuilding(tower.Id), pick.TargetCode);
        Assert.Equal(new GridPoint(tower.X, tower.Y), pick.Cell);
    }

    [Fact]
    public void A_tap_on_any_part_drawn_above_a_footprint_picks_the_building()
    {
        GameWorld world = World();
        var checkedKinds = new HashSet<int>();
        foreach (Building b in BuildingOffers.StandingBuildings(world, 1).Select(world.Building))
        {
            if (Shapes.Of(world, b, CellWidth, CellHeight).Y >= b.Y * CellHeight || !checkedKinds.Add(b.Kind))
            {
                continue;
            }
            (float x, float y) = AboveFootprint(world, b);

            MapPick pick = MapPicker.Pick(world, 0, [], x, y, 0, CellWidth, CellHeight, new AllSeen(), Shapes);

            Assert.Equal(PickKind.OtherBuilding, pick.Kind);
            Assert.Equal(b.Id, pick.Id);
            // An order aims at a cell of the footprint.
            (int width, int height) = FootprintCheck.Size(world, b.Kind);
            Assert.InRange(pick.Cell.X, b.X, b.X + width - 1);
            Assert.InRange(pick.Cell.Y, b.Y, b.Y + height - 1);
        }
        // Huts, the armoury and the towers at least have roofs above their cells.
        Assert.Superset(new HashSet<int> { RuleNumbers.Hut, RuleNumbers.Armoury, RuleNumbers.Tower }, checkedKinds);
    }

    [Fact]
    public void Where_a_picture_covers_another_buildings_footprint_the_one_in_front_is_picked()
    {
        GameWorld world = World();
        var buildings = BuildingOffers.StandingBuildings(world, 1).Select(world.Building).ToList();
        int checkedPoints = 0;
        foreach (Building front in buildings)
        {
            PixelRect picture = Shapes.Of(world, front, CellWidth, CellHeight);
            foreach (Building back in buildings.Where(b => b != front && Bottom(world, b) < Bottom(world, front)))
            {
                (int width, int height) = FootprintCheck.Size(world, back.Kind);
                // A point of the front picture's top row of pixels inside the footprint behind.
                for (int x = picture.X; x < picture.X + picture.Width; x += 2)
                {
                    int y = Math.Max(picture.Y, back.Y * CellHeight);
                    bool inBack = x >= back.X * CellWidth && x < (back.X + width) * CellWidth && y < (back.Y + height) * CellHeight;
                    if (!inBack || !picture.Contains(x, y) || y >= front.Y * CellHeight)
                    {
                        continue;
                    }
                    MapPick pick = MapPicker.Pick(world, 0, [], x, y, 0, CellWidth, CellHeight, new AllSeen(), Shapes);
                    Assert.Equal(front.Id, pick.Id);
                    checkedPoints++;
                }
            }
        }
        Assert.True(checkedPoints > 0, "the map has a picture reaching over another building");
    }

    private static int Bottom(GameWorld world, Building b) => b.Y + FootprintCheck.Size(world, b.Kind).Height;

    [Fact]
    public void Right_clicking_an_enemy_buildings_picture_sends_the_units_against_it()
    {
        GameWorld world = World();
        Building tower = Manned(world, RuleNumbers.Tower);
        (float x, float y) = AboveFootprint(world, tower);
        MapPick pick = MapPicker.Pick(world, 0, [], x, y, 0, CellWidth, CellHeight, new AllSeen(), Shapes);
        var selection = new UnitSelection();
        selection.SelectUnit(BuildingOffers.LivingUnits(world, 0)[0]);

        Assert.Equal(TapAction.Move, TapRules.Resolve(TapMode.Order, unitsSelected: true, pick, -1));
        MoveUnits order = TapRules.Move(0, selection, pick, hurry: false);
        Assert.Equal(CellCode.OfBuilding(tower.Id), order.Target);
        Assert.Equal(new GridPoint(tower.X, tower.Y), order.Cell);
    }

    [Fact]
    public void A_building_on_land_not_explored_is_not_picked()
    {
        GameWorld world = World();
        Building tower = Manned(world, RuleNumbers.Tower);
        (float x, float y) = AboveFootprint(world, tower);

        Assert.Null(MapPicker.BuildingAt(world, 0, Shapes, x, y, CellWidth, CellHeight, new NothingSeen()));
    }

    [Fact]
    public void A_villager_in_front_is_picked_first_and_a_second_tap_reaches_the_building()
    {
        GameWorld world = World();
        Building store = BuildingOffers.StandingBuildings(world, 0).Select(world.Building).First(b => b.Kind == RuleNumbers.Store);
        int villager = BuildingOffers.LivingUnits(world, 0)[0];
        float x = (store.X * CellWidth) + 30, y = (store.Y * CellHeight) + 20;
        var boxes = new List<CreatureBox> { new(MoverKind.Unit, villager, (int)x - 5, (int)y - 10, 10, 20, 1) };
        var selection = new UnitSelection();

        MapPick first = MapPicker.Pick(world, 0, boxes, x, y, 0, CellWidth, CellHeight, new AllSeen(), Shapes);
        Assert.Equal(PickKind.OwnUnit, first.Kind);
        Assert.False(TapRules.ShowsAlready(selection, first));
        selection.SelectUnit(first.Id);

        Assert.True(TapRules.ShowsAlready(selection, first));
        MapPick? behind = MapPicker.BuildingAt(world, 0, Shapes, x, y, CellWidth, CellHeight, new AllSeen());
        Assert.Equal(store.Id, behind?.Id);
        Assert.Equal(PickKind.OwnBuilding, behind?.Kind);
    }

    [Fact]
    public void A_tap_just_beside_a_villager_on_open_ground_still_takes_it()
    {
        GameWorld world = World();
        int villager = BuildingOffers.LivingUnits(world, 0)[0];
        Unit unit = world.Unit(villager);
        int x = unit.X * CellWidth, y = unit.Y * CellHeight;
        var boxes = new List<CreatureBox> { new(MoverKind.Unit, villager, x, y, 10, 16, 1) };

        MapPick pick = MapPicker.Pick(world, 0, boxes, x + 13, y + 8, 4, CellWidth, CellHeight, new AllSeen(), Shapes);

        Assert.Equal(villager, pick.Id);
    }

    [Fact]
    public void Every_picture_lies_within_the_cells_the_picker_looks_at()
    {
        GameWorld world = World();
        int kinds = Math.Min(world.Content.Rules.Buildings.Kinds.Count, RuleNumbers.LastSite + 1);
        for (int kind = 0; kind < kinds; kind++)
        {
            (int width, int height) = FootprintCheck.Size(world, kind);
            for (int level = 0; level <= 3; level++)
            {
                PixelRect picture = Shapes.Picture(world, kind, level, CellWidth, CellHeight);
                Assert.True(picture.Y >= -BuildingShapes.RowsAbove * CellHeight, $"kind {kind} level {level} reaches {picture.Y} px above");
                Assert.True(picture.X >= -BuildingShapes.CellsBeside * CellWidth, $"kind {kind} level {level} reaches {picture.X} px left");
                Assert.True(picture.X + picture.Width <= (width + BuildingShapes.CellsBeside) * CellWidth, $"kind {kind} level {level} reaches right");
                Assert.True(picture.Y + picture.Height <= (height + 1) * CellHeight, $"kind {kind} level {level} reaches below");
            }
        }
    }
}
