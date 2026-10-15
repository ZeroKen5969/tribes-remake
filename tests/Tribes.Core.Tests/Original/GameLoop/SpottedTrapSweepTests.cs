using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.Tests.Systems;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>The sweep that clears spotted traps a row per frame after a pause (the whole frame is compared with the original elsewhere).</summary>
public sealed class SpottedTrapSweepTests
{
    private const int TrapKind = 21;

    [Fact]
    public void SweepsOneRowPerFrameAfterThePause_ThenStartsOver()
    {
        GameWorld world = TestWorlds.Empty(10, 5);
        var sweep = new SpottedTrapSweep(world);
        var spotted = new GridPoint(3, 2);
        var other = new GridPoint(4, 2);
        world.Map.SetCell(spotted, new MapCell(CellContent.SpottedTrap, TrapKind, 0));
        world.Map.SetCell(other, new MapCell(CellContent.Trap, TrapKind, 0));
        for (int frame = 1; frame < SpottedTrapSweep.PauseFrames + 2; frame++)
        {
            sweep.Advance();
            Assert.Equal(CellContent.SpottedTrap, world.Map.CellAt(spotted).Content);
        }
        sweep.Advance();
        Assert.True(world.Map.CellAt(spotted).IsEmpty);
        Assert.Equal(CellContent.Trap, world.Map.CellAt(other).Content);
        sweep.Advance();
        Assert.Equal(SpottedTrapSweep.PauseFrames + 3, world.RowSweep);
        sweep.Advance();
        Assert.Equal(0, world.RowSweep);
        Assert.Throws<System.ArgumentNullException>(() => new SpottedTrapSweep(null!));
    }
}
