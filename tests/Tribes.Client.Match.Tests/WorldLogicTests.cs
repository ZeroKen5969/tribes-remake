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
/// The match screen's view of a real game: what the panels offer is exactly what the rules carry
/// out, picking finds what is drawn, footprints agree with the rules, and the scripted-input and
/// campaign-flow rules.
/// </summary>
public sealed class WorldLogicTests
{
    /// <summary>The village mission: a store, an armoury, a kitchen and huts from the start.</summary>
    private const int VillageMission = 2;

    [Fact]
    public void EveryWorkOrderThePanelOffers_IsCarriedOut()
    {
        GameSimulation game = Repo.Instance.Started(VillageMission);
        GameWorld world = game.World;
        int offered = 0;
        foreach (int id in BuildingOffers.StandingBuildings(world, 0))
        {
            foreach (BuildingWork work in BuildingOffers.Work(world, world.Building(id)))
            {
                GameSimulation copy = GameSimulation.Load(Repo.Instance.Content, game.Save());
                TickResult result = copy.Step(TickInput.WithOrders(new OrderBuildingWork(0, id, work)));
                Assert.Equal(OrderOutcome.Applied, result.Outcomes[0]);
                offered++;
            }
        }
        Assert.True(offered > 0);
    }

    [Fact]
    public void WorkNotOffered_IsRefusedByTheRules()
    {
        GameSimulation game = Repo.Instance.Started(VillageMission);
        GameWorld world = game.World;
        foreach (int id in BuildingOffers.StandingBuildings(world, 0))
        {
            IReadOnlyList<BuildingWork> offers = BuildingOffers.Work(world, world.Building(id));
            foreach (BuildingWork work in new[] { BuildingWork.StopUpgrade, BuildingWork.StopRepair })
            {
                if (offers.Contains(work))
                {
                    continue;
                }
                GameSimulation copy = GameSimulation.Load(Repo.Instance.Content, game.Save());
                Assert.NotEqual(OrderOutcome.Applied, copy.Step(TickInput.WithOrders(new OrderBuildingWork(0, id, work))).Outcomes[0]);
            }
        }
    }

    [Fact]
    public void ArmouryLines_MatchTheProductsTheRulesTake()
    {
        GameSimulation game = Repo.Instance.Started(VillageMission);
        GameWorld world = game.World;
        Building armoury = BuildingOffers.StandingBuildings(world, 0).Select(world.Building).First(b => b.Kind == RuleNumbers.Armoury);
        IReadOnlyList<ArmouryLine> lines = BuildingOffers.Armoury(world, armoury);
        Assert.Contains(lines, l => l.IsTrap);
        foreach (ArmouryLine line in lines.Where(l => !l.IsTrap))
        {
            GameSimulation copy = GameSimulation.Load(Repo.Instance.Content, game.Save());
            var order = new SetArmouryOrder(0, armoury.Id, line.Product, line.Pending + 1);
            Assert.Equal(OrderOutcome.Applied, copy.Step(TickInput.WithOrders(order)).Outcomes[0]);
            Assert.Equal(line.Pending + 1, BuildingOffers.Armoury(copy.World, copy.World.Building(armoury.Id)).First(l => l.Product == line.Product).Pending);
        }
        Assert.Equal(20, lines.First(l => l.IsTrap).TrapKind);
    }

    [Fact]
    public void FreeFootprints_AreAcceptedAndBlockedOnesRefused()
    {
        GameSimulation game = Repo.Instance.Started(VillageMission);
        GameWorld world = game.World;
        Building store = BuildingOffers.StandingBuildings(world, 0).Select(world.Building).First(b => b.Kind == RuleNumbers.Store);
        Assert.False(FootprintCheck.IsFree(world, 0, RuleNumbers.Hut, new GridPoint(store.X, store.Y)));
        GridPoint? free = null;
        for (int r = 1; r < 20 && free is null; r++)
        {
            for (int dx = -r; dx <= r && free is null; dx++)
            {
                var origin = new GridPoint(store.X + dx, store.Y + r);
                if (FootprintCheck.IsFree(world, 0, RuleNumbers.Hut, origin))
                {
                    free = origin;
                }
            }
        }
        Assert.NotNull(free);
        GameSimulation copy = GameSimulation.Load(Repo.Instance.Content, game.Save());
        Assert.Equal(OrderOutcome.Applied, copy.Step(TickInput.WithOrders(new PlaceBuilding(0, RuleNumbers.Hut, free!.Value))).Outcomes[0]);
        Assert.NotEqual(OrderOutcome.Applied, copy.Step(TickInput.WithOrders(new PlaceBuilding(0, RuleNumbers.Hut, free.Value))).Outcomes[0]);
        Assert.False(FootprintCheck.IsFree(copy.World, 0, RuleNumbers.Hut, free.Value));
    }

    [Fact]
    public void Footprint_FollowsThePointerByItsMiddle()
    {
        GameWorld world = Repo.Instance.Started(VillageMission).World;
        (int width, int height) = FootprintCheck.Size(world, RuleNumbers.Hut);
        GridPoint origin = FootprintCheck.OriginUnder(world, RuleNumbers.Hut, (10 + (width / 2f)) * 22, (20 + (height / 2f)) * 16, 22, 16);
        Assert.Equal(new GridPoint(10, 20), origin);
        Assert.Equal(width * height, FootprintCheck.Cells(world, 0, RuleNumbers.Hut, origin).Count);
    }

    [Fact]
    public void Picking_FindsTheDrawnVillagerAndTheBuildingsAndNodes()
    {
        GameWorld world = Repo.Instance.Started(VillageMission).World;
        var layer = new ObjectLayer(Repo.Instance.Assets);
        var scene = new SceneList();
        var view = new ViewRect(0, 50, 40, 40);
        layer.Compose(new SceneRequest(world, view, new ScenePresentation(), WorldMarks.None, new TribeLooks(0), false), scene);
        List<CreatureBox> boxes = MapPicker.Boxes(scene.Items, view, 22, 16);
        Assert.NotEmpty(boxes);
        CreatureBox first = boxes.First(b => b.Mover == MoverKind.Unit);
        (float cx, float cy) = first.Centre;
        // Where pictures overlap, the one drawn last (on top) is picked.
        CreatureBox villager = boxes.Where(b => b.Contains(cx, cy)).OrderBy(b => b.Order).Last();
        MapPick pick = MapPicker.Pick(world, 0, boxes, cx, cy, 0, 22, 16);
        Assert.Equal(PickKind.OwnUnit, pick.Kind);
        Assert.Equal(villager.Id, pick.Id);
        Assert.Equal(villager.Id, pick.TargetCode);
        // Smooth motion draws a walker off its rule position: its box follows the picture.
        List<CreatureBox> moved = MapPicker.Boxes(scene.Items, view, 22, 16, (mover, id) => mover == first.Mover && id == first.Id ? (5, -3) : (0, 0));
        CreatureBox shifted = moved.Single(b => b.Mover == first.Mover && b.Id == first.Id);
        Assert.Equal((first.X + 5, first.Y - 3, first.Width, first.Height), (shifted.X, shifted.Y, shifted.Width, shifted.Height));
        Building store = BuildingOffers.StandingBuildings(world, 0).Select(world.Building).First(b => b.Kind == RuleNumbers.Store);
        MapPick building = MapPicker.AtCell(world, 0, new GridPoint(store.X, store.Y));
        Assert.Equal(PickKind.OwnBuilding, building.Kind);
        Assert.Equal(-store.Id, building.TargetCode);
        ResourceNode node = world.Map.ResourceNodes.First(n => n.InUse && world.Fog(0).IsExplored(n.Position));
        Assert.Equal(PickKind.Node, MapPicker.AtCell(world, 0, node.Position).Kind);
        Assert.Equal(PickKind.Outside, MapPicker.AtCell(world, 0, new GridPoint(-1, 3)).Kind);
        List<int> boxed = MapPicker.UnitsInBox(world, 0, boxes, view.Column * 22, view.Row * 16, (view.Column + view.Columns) * 22, (view.Row + view.Rows) * 16);
        float left = view.Column * 22, top = view.Row * 16, right = (view.Column + view.Columns) * 22, bottom = (view.Row + view.Rows) * 16;
        int inside = boxes.Count(b => b.Mover == MoverKind.Unit && world.Unit(b.Id).Tribe == 0
            && b.Centre.X >= left && b.Centre.X <= right && b.Centre.Y >= top && b.Centre.Y <= bottom);
        Assert.Equal(inside, boxed.Count);
    }

    [Fact]
    public void Picking_LeavesUnexploredLandBlank()
    {
        GameWorld world = Repo.Instance.Started(VillageMission).World;
        FogOfWar fog = world.Fog(0);
        for (int y = 0; y < world.Map.PlayableSize.Height; y++)
        {
            for (int x = 0; x < world.Map.PlayableSize.Width; x++)
            {
                var cell = new GridPoint(x, y);
                if (!fog.IsExplored(cell) && !world.Map.CellAt(cell).IsEmpty)
                {
                    Assert.Equal(PickKind.Ground, MapPicker.AtCell(world, 0, cell).Kind);
                    return;
                }
            }
        }
    }

    [Fact]
    public void MovingSelectedUnits_ToAPickedNode_IsCarriedOut()
    {
        GameSimulation game = Repo.Instance.Started(VillageMission);
        GameWorld world = game.World;
        var selection = new UnitSelection();
        selection.SetUnits(BuildingOffers.LivingUnits(world, 0).Take(3));
        ResourceNode node = world.Map.ResourceNodes.First(n => n.InUse && world.Fog(0).IsVisible(n.Position));
        MapPick pick = MapPicker.AtCell(world, 0, node.Position);
        Assert.Equal(TapAction.Move, TapRules.Resolve(TapMode.Order, true, pick, -1));
        TickResult result = game.Step(TickInput.WithOrders(TapRules.Move(0, selection, pick, hurry: false)));
        Assert.Equal(OrderOutcome.Applied, result.Outcomes[0]);
    }

    [Fact]
    public void StoreAndChief_OffersFollowTheTribe()
    {
        GameWorld world = Repo.Instance.Started(VillageMission).World;
        IReadOnlyList<int> weapons = BuildingOffers.WeaponsInStock(world, 0);
        foreach (int weapon in weapons)
        {
            Assert.True(OrderMenus.InStock(world, 0, weapon));
        }
        Assert.All(BuildingOffers.ChiefCandidates(world, 0), id => Assert.True(world.Unit(id).Age >= RuleNumbers.AdultAge));
        Assert.False(BuildingOffers.AnyCarrying(world, new[] { 0, -5, 9999 }));
        Assert.All(BuildingOffers.LivingUnits(world, 0), id => Assert.Equal(0, BuildingOffers.BuildingInside(world, world.Unit(id))));
    }

    [Fact]
    public void ScriptLines_ParseCommandsAndSkipComments()
    {
        IReadOnlyList<ScriptLine> lines = ScriptLine.ParseAll("# start\nwait 30\n\ntap 12,40  # a tap\nPRESS BuildButton\n");
        Assert.Equal(3, lines.Count);
        Assert.Equal("wait", lines[0].Command);
        Assert.Equal(30, lines[0].Int(0));
        Assert.Equal(new System.Numerics.Vector2(12, 40), lines[1].Point(0));
        Assert.Equal("press", lines[2].Command);
        Assert.Equal("BuildButton", lines[2].Args[0]);
        Assert.Equal("press BuildButton", lines[2].ToString());
        Assert.Throws<System.FormatException>(() => lines[2].Int(0));
        Assert.Throws<System.FormatException>(() => lines[0].Point(0));
    }

    [Fact]
    public void BuildingLists_FollowTheWorld()
    {
        GameWorld world = Repo.Instance.Started(VillageMission).World;
        Building armoury = BuildingOffers.StandingBuildings(world, 0).Select(world.Building).First(b => b.Kind == RuleNumbers.Armoury);
        int unit = BuildingOffers.LivingUnits(world, 0)[0];
        Unit u = world.Unit(unit);
        // A villager inside: off its map cell, on the building's occupant list, its target the building.
        world.Map.SetCell(new GridPoint(u.X, u.Y), MapCell.Empty);
        u.TargetIndex = armoury.Id;
        u.ActionSubState = 1;
        u.NextOccupantOrCellObject = 0;
        armoury.FirstOccupant = unit;
        Assert.Equal(armoury.Id, BuildingOffers.BuildingInside(world, u));
        Assert.Equal(new[] { unit }, BuildingOffers.Inside(world, armoury));
        Assert.Contains(unit, BuildingOffers.Evictable(world, armoury));
        armoury.OperatorOrBeds = BuildingOffers.LivingUnits(world, 0)[1];
        Assert.Equal(armoury.OperatorOrBeds, BuildingOffers.Evictable(world, armoury)[0]);
        Assert.True(BuildingOffers.ArmouryQueue(world, 0, armoury.Id) >= 0);
        Assert.Equal(-1, BuildingOffers.ArmouryQueue(world, 0, 9999));
        // As a sanctuary the same slot offers two spells per level and no armoury lines.
        armoury.Kind = RuleNumbers.Sanctuary;
        armoury.Level = 2;
        Assert.Equal(new[] { 0, 1, 2, 3 }, BuildingOffers.Spells(world, armoury));
        Assert.Empty(BuildingOffers.Armoury(world, armoury));
        Assert.True(BuildingOffers.CanDemolish(0, armoury));
        Assert.False(BuildingOffers.CanDemolish(1, armoury));
        armoury.HitPoints = 0;
        Assert.Empty(BuildingOffers.Work(world, armoury));
        Assert.Empty(BuildingOffers.Spells(world, armoury));
    }

    [Fact]
    public void Picking_TakesTheTopmostOrNearestCreature()
    {
        GameWorld world = Repo.Instance.Started(VillageMission).World;
        int[] units = BuildingOffers.LivingUnits(world, 0).Take(2).ToArray();
        var boxes = new List<CreatureBox>
        {
            new(MoverKind.Unit, units[0], 100, 100, 20, 20, 1),
            new(MoverKind.Unit, units[1], 110, 110, 20, 20, 5),
        };
        Assert.Equal(units[1], MapPicker.Pick(world, 0, boxes, 115, 115, 0, 22, 16).Id);
        Assert.Equal(units[0], MapPicker.Pick(world, 0, boxes, 98, 99, 4, 22, 16).Id);
        Assert.Equal(PickKind.OtherUnit, MapPicker.Pick(world, 1, boxes, 105, 105, 0, 22, 16).Kind);
        Assert.NotEqual(PickKind.OwnUnit, MapPicker.Pick(world, 0, boxes, 300, 300, 4, 22, 16).Kind);
    }
}
