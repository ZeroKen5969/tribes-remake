using System.Collections.Generic;
using System.Linq;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>
/// The original's world update runs inside a try block, so an exception skips the rest of the
/// update. In rules mode none may escape, so the new frame needs no exception-driven control flow:
/// long key-monkey games, from the menus and in every mission, are scanned for escaping exceptions.
/// </summary>
[Trait("Speed", "Long")]
public sealed class WorldUpdateExceptionTests
{
    private readonly ITestOutputHelper output;

    public WorldUpdateExceptionTests(ITestOutputHelper output) => this.output = output;

    /// <summary>Monkey seeds played from the menus.</summary>
    public static IEnumerable<object[]> MenuGames => new[] { 1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L }.Select(s => new object[] { s });

    [Fact]
    public void MonkeyGamesFromTheMenus_NeverSkipPartOfAnUpdate() =>
        AssertNoneEscape(MenuGames.Select(a => new MonkeyGame((long)a[0], 30000, KeepPlaying: true)));

    [Fact]
    public void MonkeyGamesOfEveryMission_NeverSkipPartOfAnUpdate() =>
        AssertNoneEscape(Enumerable.Range(1, 11).Select(m => new MonkeyGame(m, 20000, Mission: m, Difficulty: m % 2, KeepPlaying: true)));

    [Fact]
    public void TheScan_SeesAnExceptionThatEndsAnUpdateEarly()
    {
        const int broken = 300;
        sbyte[][]? cells = null;
        ExceptionScan scan = GameComparison.WorldUpdateExceptions(WorldFrameParityTests.Resources, new MonkeyGame(1, 3000),
            (update, game) =>
            {
                if (update == broken)
                {
                    // The world's view of the map shrinks to one cell: any cell lookup throws.
                    cells = game.world_a.bArr2_b;
                    game.world_a.bArr2_b = new[] { new sbyte[1] };
                }
                else if (update == broken + 1)
                {
                    game.world_a.bArr2_b = cells;
                }
            });
        output.WriteLine(scan.ToString());
        Assert.Equal(1, scan.UpdatesWithThrows);
        Assert.True(scan.Updates > broken, scan.ToString());
    }

    private void AssertNoneEscape(IEnumerable<MonkeyGame> games)
    {
        int updates = 0, escaped = 0;
        var report = new List<string>();
        foreach (MonkeyGame game in games)
        {
            ExceptionScan scan = GameComparison.WorldUpdateExceptions(WorldFrameParityTests.Resources, game);
            output.WriteLine($"{game}: {scan}");
            updates += scan.Updates;
            escaped += scan.EscapedCount;
            if (scan.EscapedCount > 0)
            {
                report.Add($"{game}: {scan}");
            }
        }
        output.WriteLine($"{updates} updates scanned, {escaped} escaped");
        Assert.True(escaped == 0, string.Join("\n", report));
    }
}
