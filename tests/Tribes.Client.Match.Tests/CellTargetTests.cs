using System.Collections.Generic;
using System.Linq;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.World.Scene;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Xunit;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>
/// Cell codes of units and buildings past the original's 100 and 60 slots (tribes 2 and on in a
/// match) lie outside the original's byte range: picking one aims an order at its real code, and
/// the panels find the right unit, building, animal or node from any code.
/// </summary>
public sealed class CellTargetTests
{
    /// <summary>Ten tribes of people: more than the original's 100 unit slots in use.</summary>
    private static GameWorld ManyTribes() =>
        GameSimulation.StartEndless(Repo.Instance.Content, new EndlessSetup
        {
            Seed = 5,
            Tribes = Enumerable.Range(0, 10).Select(h => new MatchTribe(TribeController.Human(h))).ToArray(),
            ComputerTribes = 0,
            NewTribes = 0,
        }).World;

    [Fact]
    public void Picking_a_unit_past_the_original_slots_aims_at_its_own_code()
    {
        GameWorld world = ManyTribes();
        Assert.True(world.UnitSlots > CellCode.OriginalUnits);
        int unit = Enumerable.Range(CellCode.OriginalUnits + 1, world.UnitSlots - CellCode.OriginalUnits)
            .First(id => world.Unit(id).HitPoints > 0 && world.Unit(id).Tribe != 0);
        var boxes = new List<CreatureBox> { new(MoverKind.Unit, unit, 100, 100, 20, 20, 1) };

        MapPick pick = MapPicker.Pick(world, 0, boxes, 105, 105, 0, 22, 16);

        Assert.Equal(PickKind.OtherUnit, pick.Kind);
        Assert.Equal(CellCode.OfUnit(unit), pick.TargetCode);
        Assert.Equal(unit, CellTargets.UnitOf(world, pick.TargetCode));
        Assert.Null(CellTargets.AnimalOf(world, pick.TargetCode));
    }

    [Fact]
    public void A_shown_unit_of_another_tribe_is_let_go_when_it_is_gone()
    {
        GameWorld world = ManyTribes();
        int unit = Enumerable.Range(CellCode.OriginalUnits + 1, world.UnitSlots - CellCode.OriginalUnits)
            .First(id => world.Unit(id).HitPoints > 0 && world.Unit(id).Tribe != 0);
        var selection = new UnitSelection();
        selection.ShowOther(CellCode.OfUnit(unit));

        selection.Forget(unit);

        Assert.Equal(SelectionKind.None, selection.Kind);
    }

    [Fact]
    public void Animals_are_aimed_at_by_their_code()
    {
        GameWorld world = ManyTribes();
        int animal = world.AnimalSlots - 1;
        var boxes = new List<CreatureBox> { new(MoverKind.Animal, animal, 100, 100, 20, 20, 1) };
        world.Animal(animal).HitPoints = 10;

        MapPick pick = MapPicker.Pick(world, 0, boxes, 105, 105, 0, 22, 16);

        Assert.Equal(PickKind.Animal, pick.Kind);
        Assert.Equal(CellCode.OfAnimal(animal), pick.TargetCode);
        Assert.Equal(animal, CellTargets.AnimalOf(world, pick.TargetCode));
        Assert.Null(CellTargets.UnitOf(world, pick.TargetCode));
    }

    [Fact]
    public void Codes_of_every_kind_lead_to_their_slot()
    {
        GameWorld world = ManyTribes();
        int lastUnit = world.UnitSlots, lastBuilding = world.BuildingSlots;

        Assert.Equal(5, CellTargets.UnitOf(world, 5));
        Assert.Equal(lastUnit, CellTargets.UnitOf(world, CellCode.OfUnit(lastUnit)));
        Assert.Equal(3, CellTargets.BuildingOf(world, CellCode.OfBuilding(3)));
        Assert.Equal(lastBuilding, CellTargets.BuildingOf(world, CellCode.OfBuilding(lastBuilding)));
        Assert.Equal(0, CellTargets.AnimalOf(world, CellCode.FirstAnimal));
        Assert.Equal(0, CellTargets.NodeOf(world, CellCode.FirstNode));
        // A code of another kind, or a slot the world does not have, is nothing.
        Assert.Null(CellTargets.BuildingOf(world, CellCode.OfUnit(5)));
        Assert.Null(CellTargets.UnitOf(world, CellCode.OfUnit(lastUnit + 1)));
        Assert.Null(CellTargets.BuildingOf(world, CellCode.OfBuilding(lastBuilding + 1)));
        Assert.Null(CellTargets.NodeOf(world, CellCode.OfNode(world.Map.ResourceNodes.Count)));
        Assert.Null(CellTargets.UnitOf(world, CellCode.Empty));
    }
}
