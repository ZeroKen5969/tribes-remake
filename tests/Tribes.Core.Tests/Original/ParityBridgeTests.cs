using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.World;
using Tribes.Parity;
using Xunit;

namespace Tribes.Core.Tests;

/// <summary>The bridge between the original's state and the new world loses nothing.</summary>
public sealed class ParityBridgeTests
{
    private static readonly string Root = TestPaths.RepoRoot();
    private static readonly GameContent Content = GameContent.Load(Path.Combine(Root, "data"));

    [Theory]
    [InlineData(1L)]
    [InlineData(4L)]
    [InlineData(6L)]
    public void ImportThenCapture_GivesTheOriginalsState(long seed)
    {
        var failures = new List<string>();
        int checkedFrames = 0, framesInPlay = 0;

        Tribes.Trace.Program.Run(Path.Combine(Root, "original", "game", "res"), 8000, seed, 80, (frame, game) =>
        {
            if (frame < 600 || frame % 400 != 0 || failures.Count > 0)
            {
                return;
            }
            RuleState expected = LegacyRules.Capture(game);
            GameWorld world = LegacyRules.Import(game, Content);
            IReadOnlyList<string> diff = expected.Differences(WorldRules.Capture(world), 10);
            if (diff.Count > 0)
            {
                failures.Add($"frame {frame}: " + string.Join("; ", diff));
            }
            checkedFrames++;
            if (Enumerable.Range(1, world.UnitSlots).Any(id => world.Unit(id).InUse)
                && Enumerable.Range(1, world.BuildingSlots).Any(id => world.Building(id).InUse))
            {
                framesInPlay++;
            }
        });

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.True(checkedFrames > 10, $"only {checkedFrames} frames checked");
        Assert.True(framesInPlay > 5, $"only {framesInPlay} of {checkedFrames} frames had units and buildings");
    }

    [Fact]
    public void SaveThenLoad_KeepsTheWholeWorld()
    {
        var failures = new List<string>();
        int checkedFrames = 0;

        Tribes.Trace.Program.Run(Path.Combine(Root, "original", "game", "res"), 6000, 4L, 80, (frame, game) =>
        {
            if (frame < 1000 || frame % 1000 != 0)
            {
                return;
            }
            GameWorld world = LegacyRules.Import(game, Content);
            var stream = new MemoryStream();
            WorldSnapshot.Save(world, stream);
            stream.Position = 0;
            GameWorld loaded = WorldSnapshot.Load(stream, Content);

            IReadOnlyList<string> diff = WorldRules.Capture(world).Differences(WorldRules.Capture(loaded), 10);
            if (diff.Count > 0 || WorldSnapshot.Hash(world) != WorldSnapshot.Hash(loaded))
            {
                failures.Add($"frame {frame}: " + string.Join("; ", diff));
            }
            loaded.Unit(1).Happiness += 1;
            if (WorldSnapshot.Hash(world) == WorldSnapshot.Hash(loaded))
            {
                failures.Add($"frame {frame}: hash did not change");
            }
            checkedFrames++;
        });

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Assert.Equal(6, checkedFrames);
    }

    [Fact]
    public void Load_RejectsOtherData()
    {
        Assert.Throws<InvalidDataException>(() => WorldSnapshot.Load(new MemoryStream(new byte[16]), Content));
    }
}
