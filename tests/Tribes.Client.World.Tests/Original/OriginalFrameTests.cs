using System.IO;
using Tribes.Client.World.Scene;
using Tribes.Core.World;
using Tribes.Graphics.Imaging;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Client.World.Tests;

/// <summary>
/// The world view's picture against the original's frames (original/reference/frames, written by
/// original/tools/Tribes.DevSnapshots/reference.sh); built only when the original repository is in original/.
/// </summary>
public sealed class OriginalFrameTests
{
    private readonly ITestOutputHelper output;

    public OriginalFrameTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void EveryDevSnapshotIsDrawnLikeTheOriginalsFrame()
    {
        Repo repo = Repo.Instance;
        int frames = 0;
        long compared = 0, stale = 0;
        foreach (string snapshot in repo.Snapshots())
        {
            (GameWorld world, SnapshotView view) = repo.Load(snapshot);
            string reference = repo.OriginalReference("frames", $"s{view.Seed}", $"f{view.Frame:D5}.png");
            Assert.True(File.Exists(reference), "missing " + reference + " (run original/tools/Tribes.DevSnapshots/reference.sh)");
            RgbaImage screen = FrameCheck.DrawScreen(world, view, repo.Assets, repo.Sheet);
            FrameCheckResult result = FrameCheck.Measure(view, Png.Decode(File.ReadAllBytes(reference)), screen,
                FrameCheck.StaleCells(repo.OriginalReference("frames", $"s{view.Seed}", "terrain.txt"), view.Frame),
                FrameCheck.ObjectCoverage(world, view, repo.Assets, repo.Sheet));
            Assert.True(result.Unexplained == 0, $"{Path.GetFileName(snapshot)}: {result.Unexplained} pixels differ from the original");
            Assert.True(result.Compared > 40000, $"{Path.GetFileName(snapshot)}: only {result.Compared} pixels compared");
            frames++;
            compared += result.Compared;
            stale += result.TerrainHistory;
        }
        output.WriteLine($"{frames} frames, {compared} pixels compared; {stale} differ where the original's terrain buffer kept earlier pictures");
        Assert.True(frames >= 12);
    }
}
