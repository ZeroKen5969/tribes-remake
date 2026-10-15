using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="GroupSpawner"/> with the original.</summary>
public sealed class GroupSpawnerTests
{
    private static readonly GridPoint[] Cells = { new(10, 10), new(30, 20), new(2, 2) };

    private readonly ITestOutputHelper output;

    public GroupSpawnerTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Spawn_MatchesTheOriginal()
    {
        var run = new ParityRun();
        int i = 0;
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            GridPoint cell = Cells[i % Cells.Length];
            int tribe = i % 2, weapon = i % 6, count = 1 + (i % 5);
            bool wave = i++ % 3 == 0;
            run.Check(sample, $"Group {count}:{tribe}:{wave}", g =>
            {
                (g.world_a.i_aU, g.world_a.i_y, g.world_a.i_u) = (wave ? 0 : -1, 7, tribe);
                g.world_a.a_IIIBB(count, cell.X, cell.Y, (sbyte)tribe, (sbyte)weapon);
            }, s => s.GroupSpawner.Spawn(count, cell, tribe, weapon, wave ? 7 : null));
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 30);
    }
}
