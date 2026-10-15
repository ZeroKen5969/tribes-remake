using System;
using System.IO;
using System.Linq;
using System.Text;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>The tribe model: controllers, relations, roles, growing worlds, cell codes and saves.</summary>
public sealed class TribeModelTests
{
    private static GameWorld NewWorld(int tribes) =>
        new(MatchWorlds.Content, new WorldMap(new GridSize(96, 96), new GridSize(96, 96)), WorldLayout.ForTribes(tribes), new GameRandom(1));

    [Fact]
    public void OriginalLayout_KeepsTheOriginalRoles()
    {
        GameWorld world = NewWorld(2);
        Assert.Equal(WorldMode.Campaign, world.Mode);
        Assert.Equal(0, TribeRoles.Player(world));
        Assert.Equal(1, TribeRoles.Computer(world));
        Assert.True(TribeRoles.IsPlayer(world, 0) && !TribeRoles.IsPlayer(world, 1) && !TribeRoles.IsPlayer(world, 2));
        Assert.True(TribeRoles.IsComputer(world, 1) && !TribeRoles.IsComputer(world, 0) && !TribeRoles.IsComputer(world, -1));
        Assert.Equal(1, TribeRoles.Enemy(world, 0));
        Assert.Equal(0, TribeRoles.Enemy(world, 1));
        Assert.Same(world.Tribes[0].Army, world.Orders);
        Assert.Same(world.Tribes[0].Hints, world.Hints);
        Assert.Same(world.Tribes[1].Ai, world.Ai);
        Assert.Same(world.Orders, TribeRoles.Army(world, 1));
        Assert.Same(world.Ai, TribeRoles.Ai(world, 0));
        Assert.Equal(1, TribeRoles.CreditedFor(world, 0, -1));
        Assert.Equal(GameWorld.DustSlots, world.Dust.Count);
        Assert.Equal(GameWorld.CoupleSlots, world.Couples.Count);
        Assert.Equal(SectorIndex.Categories, world.Sectors.CategoryCount);
    }

    [Fact]
    public void Controllers_DecideRoles()
    {
        GameWorld world = NewWorld(4);
        world.SetController(1, TribeController.Human(3));
        world.SetController(2, TribeController.Absent(2));
        world.SetController(3, TribeController.Forfeited(1));
        Assert.Equal(new[] { 0, 1, 2, 3 }, TribeRoles.HumanTribes(world));
        Assert.Empty(TribeRoles.ComputerTribes(world));
        Assert.True(TribeRoles.TakesOrders(world, 1) && !TribeRoles.TakesOrders(world, 2) && !TribeRoles.TakesOrders(world, 3));
        Assert.Equal(1, TribeRoles.Computer(world));
        Assert.Equal(3, world.Tribes[1].Controller.Slot);
        world.SetController(0, TribeController.Computer(1));
        world.Difficulty = 0;
        Assert.Equal(1, TribeRoles.Difficulty(world, 0));
        Assert.Equal(0, TribeRoles.Difficulty(world, 1));
        Assert.Equal(1, TribeRoles.Player(world));
        Assert.Throws<ArgumentOutOfRangeException>(() => TribeController.Human(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TribeController.Computer(-2));
    }

    [Fact]
    public void Relations_FollowTeamsAndAlliances()
    {
        GameWorld world = NewWorld(4);
        world.SetTeam(0, 1);
        world.SetTeam(1, 1);
        Assert.True(world.Relations.AreAllies(0, 1));
        Assert.False(world.Relations.AreEnemies(0, 1));
        Assert.True(world.Relations.AreEnemies(0, 2));
        Assert.False(world.Relations.AreEnemies(2, 2));
        Assert.False(world.Relations.AreEnemies(2, 9));
        world.Relations.SetAlliance(3, 2, true);
        Assert.True(world.Relations.AreAllies(2, 3));
        Assert.Equal(new[] { (2, 3) }, world.Relations.Alliances);
        Assert.Equal(new[] { 2, 3 }, TribeRoles.EnemiesOf(world, 0));
        world.Mode = WorldMode.Match;
        Assert.Equal(-1, TribeRoles.CreditedFor(world, 0, 1));
        Assert.Equal(2, TribeRoles.CreditedFor(world, 0, 2));
        Assert.Equal(2, TribeRoles.Enemy(world, 0));
        world.Relations.SetAlliance(2, 3, false);
        Assert.True(world.Relations.AreEnemies(2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Relations.SetAlliance(1, 1, true));
        world.SetTeam(1, -5);
        Assert.Equal(TribeState.NoTeam, world.Tribes[1].Team);
    }

    [Fact]
    public void AddTribe_AppendsSlotsAndTables()
    {
        GameWorld world = NewWorld(2);
        Unit lastUnit = world.Unit(world.UnitSlots);
        int tribe = world.AddTribe(TribeController.Computer(), 4);
        Assert.Equal(2, tribe);
        Assert.Equal(3, world.Layout.Tribes);
        Assert.Same(lastUnit, world.Unit(100));
        Assert.Equal(2, world.Unit(world.FirstUnitSlot(2)).Tribe);
        Assert.Equal(2, world.Building(world.BuildingSlots).Tribe);
        Assert.Equal(GameWorld.DustSlots, world.Dust.Count);
        Assert.Equal(GameWorld.ObjectGroupsFor(3), world.Sectors.CategoryCount);
        Assert.Equal(4, world.Tribes[2].Team);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Unit(world.UnitSlots + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldLayout.ForTribes(0));
    }

    [Fact]
    public void CellCodes_OfFurtherTribes_StayOutsideTheOriginalRange()
    {
        var layout = WorldLayout.ForTribes(40);
        foreach (int unit in new[] { 1, 100, 101, 150, 2000 })
        {
            int code = CellCode.OfUnit(unit);
            Assert.Equal(unit, CellCode.UnitOf(code));
            Assert.Equal(MapCell.UnitAt(unit), CellCode.FromCode(code, layout));
            Assert.Equal(0, CellCode.BuildingOf(code));
            Assert.Equal(-1, CellCode.AnimalOf(code));
        }
        foreach (int building in new[] { 1, 60, 61, 1200 })
        {
            int code = CellCode.OfBuilding(building);
            Assert.Equal(building, CellCode.BuildingOf(code));
            Assert.Equal(MapCell.BuildingAt(building), CellCode.FromCode(code, layout));
            Assert.Equal(-1, CellCode.NodeOf(code));
        }
        foreach (int node in new[] { 0, 49, 50, 900 })
        {
            Assert.Equal(node, CellCode.NodeOf(CellCode.OfNode(node)));
        }
        foreach (int animal in new[] { 0, 26, 27, 400 })
        {
            Assert.Equal(animal, CellCode.AnimalOf(CellCode.OfAnimal(animal)));
            Assert.True(CellCode.IsCreature(CellCode.OfAnimal(animal)));
        }
        foreach (int tribe in new[] { 0, 1, 2, 39 })
        {
            for (int kind = CellCode.FirstTrapKind; kind < CellCode.FirstTrapKind + CellCode.TrapKinds; kind++)
            {
                int code = CellCode.Trap(tribe, kind);
                Assert.Equal((tribe, kind), (CellCode.TrapOwnerOf(code), CellCode.TrapKindOf(code)));
                Assert.Equal(new MapCell(CellContent.Trap, kind, tribe), CellCode.FromCode(code, layout));
                Assert.Equal(code, CellCode.ToCode(new MapCell(CellContent.Trap, kind, tribe)));
                Assert.True(CellCode.IsStandingObject(code) && CellCode.IsCatching(code) && CellCode.IsSetTrap(code));
                Assert.True(CellCode.IsSteppingObject(CellCode.SteppingValue(code)));
                Assert.Equal(0, CellCode.BuildingOf(code));
            }
        }
        Assert.Equal(CellCode.Reserved, CellCode.SteppingValue(CellCode.Reserved));
        Assert.False(CellCode.IsSteppingObject(-CellCode.OfUnit(140)));
        Assert.False(CellCode.IsStandingObject(-CellCode.OfUnit(140)));
        Assert.Equal(140, CellCode.UnitOrOriginal(CellCode.OfUnit(140)));
        Assert.Equal(-5, CellCode.UnitOrOriginal(-5));
        Assert.Equal(140, CellCode.SlotOf(CellCode.OfUnit(140), 1, false));
        Assert.Equal(70, CellCode.SlotOf(CellCode.OfBuilding(70), -1, false));
        Assert.Equal(30, CellCode.SlotOf(CellCode.OfAnimal(30), 1, true));
        Assert.Equal(3, CellCode.SlotOf(-3, -1, false));
        Assert.Throws<InvalidDataException>(() => CellCode.FromCode(CellCode.OfUnit(2001), layout));
    }

    [Fact]
    public void ObjectGroups_NumberFurtherTribesAfterTheOriginalFive()
    {
        Assert.Equal((0, 2), (ObjectGroupTable.Units(0), ObjectGroupTable.Buildings(0)));
        Assert.Equal((1, 3), (ObjectGroupTable.Units(1), ObjectGroupTable.Buildings(1)));
        Assert.Equal((5, 6), (ObjectGroupTable.Units(2), ObjectGroupTable.Buildings(2)));
        Assert.Equal((7, 8), (ObjectGroupTable.Units(3), ObjectGroupTable.Buildings(3)));
        for (int tribe = 0; tribe < 6; tribe++)
        {
            Assert.Equal((ObjectGroupKind.Units, tribe), (ObjectGroupTable.KindOf(ObjectGroupTable.Units(tribe)), ObjectGroupTable.TribeOf(ObjectGroupTable.Units(tribe))));
            Assert.Equal((ObjectGroupKind.Buildings, tribe), (ObjectGroupTable.KindOf(ObjectGroupTable.Buildings(tribe)), ObjectGroupTable.TribeOf(ObjectGroupTable.Buildings(tribe))));
        }
        Assert.Equal(ObjectGroupKind.Animals, ObjectGroupTable.KindOf(ObjectGroupTable.AnimalGroup));
        Assert.Equal(-1, ObjectGroupTable.TribeOf(ObjectGroupTable.AnimalGroup));
        GameWorld world = NewWorld(3);
        var groups = new ObjectGroupTable(world);
        Assert.Equal(7, groups.Count);
        Assert.Equal((50, 101, 150), (groups.Slots(5).Count, groups.Slots(5)[0], groups.Slots(5)[49]));
        Assert.Equal((30, 61, 90), (groups.Slots(6).Count, groups.Slots(6)[0], groups.Slots(6)[29]));
        Assert.Equal((22, 0, 21), (groups.Slots(ObjectGroupTable.AnimalGroup).Count, groups.Slots(4)[0], groups.Slots(4)[21]));
        world.AddTribe(TribeController.Computer());
        Assert.Equal(9, groups.Count);
        Assert.Equal(151, groups.Slots(7)[0]);
    }

    [Fact]
    public void AlarmMarks_AreKeptPerTribe()
    {
        var alarm = new AlarmCall();
        alarm.MarkKnownTo(0);
        alarm.MarkKnownTo(5);
        alarm.MarkKnownTo(AlarmCall.MarkableTribes);
        Assert.Equal(1 | (1 << 5), alarm.Spotted);
        Assert.True(alarm.IsKnownTo(5) && !alarm.IsKnownTo(1) && !alarm.IsKnownTo(-1) && alarm.IsKnownTo(AlarmCall.MarkableTribes));
        Assert.False(alarm.IsKnownTo(AlarmCall.MarkableTribes + 1));
    }

    [Fact]
    public void FormatOneSaves_StillLoad()
    {
        GameSimulation game = GameSimulation.StartMission(MatchWorlds.Content, 2, 1);
        for (int tick = 0; tick < 1500; tick++)
        {
            game.Step(TickInput.WithOrders(new AdvanceScene(0)));
        }
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            GameWorld world = game.World;
            writer.Write(0x57425254);
            writer.Write(1);
            foreach (int value in new[]
            {
                world.Layout.Tribes, world.Layout.UnitsPerTribe, world.Layout.BuildingsPerTribe, world.Layout.Animals,
                world.Layout.FogSize.Width, world.Layout.FogSize.Height, world.Map.Size.Width, world.Map.Size.Height,
                world.Map.ResourceNodes.Count,
            })
            {
                writer.Write(value);
            }
            world.Exchange(new StateWriter(writer) { Format = 1 });
        }
        stream.Position = 0;
        GameWorld loaded = WorldSnapshot.Load(stream, MatchWorlds.Content);
        Assert.Equal(WorldSnapshot.Hash(game.World), WorldSnapshot.Hash(loaded));
        Assert.Equal(WorldMode.Campaign, loaded.Mode);
        stream.Position = 4;
        stream.WriteByte(9);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => WorldSnapshot.Load(stream, MatchWorlds.Content));
    }

    [Fact]
    public void FormatTwoSaves_KeepEveryTribesState()
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 6, MatchWorlds.Controllers(2, 2), teams: [3, 3, -1, -1]));
        game.World.Relations.SetAlliance(2, 3, true);
        game.SetController(1, TribeController.Absent(1));
        for (int tick = 0; tick < 2000; tick++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 6));
        }
        GameSimulation loaded = GameSimulation.Load(MatchWorlds.Content, game.Save());
        Assert.Equal(game.StateHash(), loaded.StateHash());
        Assert.Equal(WorldMode.Match, loaded.World.Mode);
        Assert.Equal(TribeControl.Absent, loaded.World.Tribes[1].Controller.Kind);
        Assert.True(loaded.World.Relations.AreAllies(2, 3) && loaded.World.Relations.AreAllies(0, 1));
        Assert.Equal(game.World.Tribes.Select(t => t.Camp), loaded.World.Tribes.Select(t => t.Camp));
        Assert.Equal(game.World.Tribes.Select(t => t.Ai.Route.Count), loaded.World.Tribes.Select(t => t.Ai.Route.Count));
    }
}
