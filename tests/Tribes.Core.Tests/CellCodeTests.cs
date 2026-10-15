using System.IO;
using Tribes.Core.Maps;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests;

/// <summary>The original's cell codes convert to model cells and back.</summary>
public sealed class CellCodeTests
{
    [Fact]
    public void EveryCodeOfTheOriginal_RoundTrips()
    {
        for (int code = -128; code <= 122; code++)
        {
            if (code is -120 or -121 or -122)
            {
                Assert.Throws<InvalidDataException>(() => CellCode.FromCode(code, WorldLayout.Original));
                continue;
            }
            MapCell cell = CellCode.FromCode(code, WorldLayout.Original);
            Assert.Equal(code, CellCode.ToCode(cell));
        }
    }

    [Fact]
    public void TheComputersPendingTrap_ReadsAsImpassable()
    {
        Assert.Equal(CellCode.Impassable, CellCode.ToCode(new MapCell(CellContent.PendingTrap, 0, 1)));
        Assert.Equal(CellCode.PlayerPendingTrap, CellCode.ToCode(new MapCell(CellContent.PendingTrap, 0, 0)));
    }

    [Fact]
    public void TrapCodes_FollowTribeAndKind()
    {
        Assert.Equal(-111, CellCode.Trap(0, 20));
        Assert.Equal(-119, CellCode.Trap(1, 22));
        Assert.Equal(new MapCell(CellContent.Trap, 22, 1), CellCode.FromCode(-119, WorldLayout.Original));
    }

    [Fact]
    public void UnknownCodes_AreRejected()
    {
        Assert.Throws<InvalidDataException>(() => CellCode.FromCode(127, WorldLayout.Original));
        Assert.Throws<InvalidDataException>(() => CellCode.ToCode(new MapCell((CellContent)99, 0, 0)));
    }
}
