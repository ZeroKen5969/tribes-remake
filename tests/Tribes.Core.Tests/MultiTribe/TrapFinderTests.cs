using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// A spotted trap remembers who found it: in a match the finder's tribe (so each tribe is shown the
/// traps it found), in the campaign the player, as the original's codes say; cell codes keep it.
/// </summary>
public sealed class TrapFinderTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 3)]
    public void SpottedTrap_InAMatch_IsTheFindersTribe(int finder, int setter)
    {
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 5, MatchWorlds.Controllers(2, 2)));
        GameWorld world = game.World;
        Unit scout = world.UnitsOf(finder).First(u => u.HitPoints > 0 && u.Age >= UnitAges.Adult);
        GridPoint trap = EmptyCellNear(world, scout, 2);
        world.Map.SetCell(trap, new MapCell(CellContent.Trap, 21, setter));
        (scout.ScoutingSkill, scout.Action) = (10, ActionNumbers.ChildIdle);
        world.Frame = scout.Id & 7;

        game.Rules.UnitStealth.Update(new UnitTurn(scout, 0));

        MapCell spotted = world.Map.CellAt(trap);
        Assert.Equal(new MapCell(CellContent.SpottedTrap, 21, finder), spotted);
        int code = CellCode.ToCode(spotted);
        Assert.Equal(finder, CellCode.SpottedTrapFinderOf(code));
        Assert.Equal(spotted, CellCode.FromCode(code, world));
    }

    [Fact]
    public void SpottedTrapCodes_RoundTrip_AndAreNoOtherCode()
    {
        GameWorld world = GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 5, MatchWorlds.Controllers(2, 6))).World;
        foreach (int finder in Enumerable.Range(0, world.Tribes.Count))
        {
            for (int kind = CellCode.FirstTrapKind; kind < CellCode.FirstTrapKind + CellCode.TrapKinds; kind++)
            {
                var cell = new MapCell(CellContent.SpottedTrap, kind, finder);
                int code = CellCode.SpottedTrap(finder, kind);
                Assert.Equal(code, CellCode.ToCode(cell));
                Assert.Equal(cell, CellCode.FromCode(code, world));
                Assert.Equal((finder, kind), (CellCode.SpottedTrapFinderOf(code), CellCode.SpottedTrapKindOf(code)));
                Assert.Equal(-1, CellCode.TrapOwnerOf(code));
                Assert.Equal((0, 0, -1, -1), (CellCode.UnitOf(code), CellCode.BuildingOf(code), CellCode.NodeOf(code), CellCode.AnimalOf(code)));
                Assert.False(CellCode.IsSteppingObject(-code) && finder > 0);
                Assert.Equal(finder == 0, code is >= sbyte.MinValue and <= sbyte.MaxValue);
            }
        }
        Assert.Equal(-1, CellCode.SpottedTrapFinderOf(CellCode.Trap(4, 20)));
        Assert.Equal(-1, CellCode.SpottedTrapFinderOf(CellCode.FirstTrap - 9));
        Assert.Equal(0, CellCode.SpottedTrapFinderOf(CellCode.FirstSpottedTrap));
    }

    [Fact]
    public void SpottedTrap_InTheCampaign_IsThePlayers_WhoeverFoundIt()
    {
        GameSimulation game = SlotGrowthTests.Campaign();
        GameWorld world = game.World;
        Unit scout = world.UnitsOf(1).First(u => u.HitPoints > 0 && u.Age >= UnitAges.Adult);
        GridPoint trap = EmptyCellNear(world, scout, 2);
        world.Map.SetCell(trap, new MapCell(CellContent.Trap, 20, 0));
        (scout.ScoutingSkill, scout.Action) = (10, ActionNumbers.ChildIdle);
        world.Frame = scout.Id & 7;

        game.Rules.UnitStealth.Update(new UnitTurn(scout, 0));

        Assert.Equal(new MapCell(CellContent.SpottedTrap, 20, 0), world.Map.CellAt(trap));
        Assert.Equal(CellCode.FirstSpottedTrap, CellCode.At(world.Map, trap));
    }

    private static GridPoint EmptyCellNear(GameWorld world, Unit unit, int distance)
    {
        foreach (var cell in new[]
        {
            new GridPoint(unit.X + distance, unit.Y), new GridPoint(unit.X - distance, unit.Y),
            new GridPoint(unit.X, unit.Y + distance), new GridPoint(unit.X, unit.Y - distance),
        })
        {
            if (world.Map.InPlay(cell) && world.Map.CellAt(cell).IsEmpty)
            {
                return cell;
            }
        }
        throw new Xunit.Sdk.XunitException("no empty cell near the scout");
    }
}
