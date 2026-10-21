using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Client.World.Scene;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Client.World.Tests;

/// <summary>
/// The world view's picture of the dev snapshots (worlds of the original's maps,
/// original/reference/snapshots) against the original's: its frames (original/reference/frames, written by
/// original/tools/Tribes.DevSnapshots/reference.sh), arcs and place squares. Built only when the
/// original repository is in original/.
/// </summary>
public sealed class WorldSceneTests
{
    private readonly ITestOutputHelper output;

    public WorldSceneTests(ITestOutputHelper output)
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

    [Fact]
    public void ProjectileArcsWorkedOutFromTheThrowerEqualTheOriginals()
    {
        Repo repo = Repo.Instance;
        int arcs = 0;
        foreach (string snapshot in repo.Snapshots())
        {
            (GameWorld world, SnapshotView view) = repo.Load(snapshot);
            foreach (SnapshotArc expected in view.Arcs)
            {
                Unit thrower = world.Unit(expected.Unit);
                (int height, int drop) = ScenePresentation.ThrowHeights(thrower.Action);
                ProjectileArc? arc = ScenePresentation.ArcOf(world, thrower, height, drop);
                Assert.NotNull(arc);
                for (int step = 0; step <= thrower.ProjectileFlightSteps; step++)
                {
                    Assert.Equal(expected.Heights[step], arc!.Heights[step]);
                    Assert.Equal(expected.Frames[step], arc.Frames[step]);
                }
                arcs++;
            }
        }
        Assert.True(arcs > 0, "no snapshot has a projectile in flight");
    }

    [Fact]
    public void TheSnapshotsShowEveryKindOfThing()
    {
        Repo repo = Repo.Instance;
        var movers = new HashSet<MoverKind>();
        int effects = 0, labels = 0, skins = 0;
        foreach (string snapshot in repo.Snapshots())
        {
            (GameWorld world, SnapshotView view) = repo.Load(snapshot);
            var list = new SceneList();
            new ObjectLayer(repo.Assets).Compose(new SceneRequest(world, view.View, view.Presentation(), view.Marks,
                new TribeLooks(0, world.Layout.Tribes), true), list);
            foreach (SceneItem item in list.Items)
            {
                movers.Add(item.Mover);
                labels += item.Part.Sheet == "pi0_26" ? 1 : 0;
                skins += item.SkinTribe == 1 ? 1 : 0;
            }
            effects += view.Effects.Count(e => e[0] > 0);
        }
        Assert.Contains(MoverKind.Unit, movers);
        Assert.Contains(MoverKind.Animal, movers);
        Assert.Contains(MoverKind.Projectile, movers);
        Assert.True(effects > 0 && labels > 0 && skins > 0, $"effects {effects}, number glyphs {labels}, computer body parts {skins}");
    }

    [Fact]
    public void TheCompositionNeverChangesTheWorld()
    {
        Repo repo = Repo.Instance;
        string snapshot = repo.Snapshots().First(s => s.Contains("s6-f16052", StringComparison.Ordinal));
        (GameWorld world, SnapshotView view) = repo.Load(snapshot);
        ulong before = WorldSnapshot.Hash(world);
        FrameCheck.DrawScreen(world, view, repo.Assets, repo.Sheet);
        Assert.Equal(before, WorldSnapshot.Hash(world));
    }

    [Fact]
    public void APointedBuildingShowsWhatTheOriginalShowsUnderItsCursor()
    {
        // A match has no cursor: it passes the selected building (and, with a mouse, the one pointed
        // at) as pointed, and that building gets its place squares as under the original's cursor.
        Repo repo = Repo.Instance;
        RgbColor used = RgbColor.Parse("#f6a800"), free = RgbColor.Parse("#40331d");
        int Squares(GameWorld world, SnapshotView view, IReadOnlySet<int> pointed)
        {
            var list = new SceneList();
            WorldMarks marks = view.Marks with { CursorCell = null, PointedBuildings = pointed };
            new ObjectLayer(repo.Assets).Compose(new SceneRequest(world, view.View, view.Presentation(), marks,
                new TribeLooks(0, world.Layout.Tribes), true), list);
            return list.Items.Count(i => i.Part.Kind == DrawPartKind.Fill && (i.Part.Color == used || i.Part.Color == free));
        }

        int checkedBuildings = 0;
        foreach (string snapshot in repo.Snapshots())
        {
            (GameWorld world, SnapshotView view) = repo.Load(snapshot);
            ViewRect seen = view.View;
            // The player's buildings on screen that have places (the store has none).
            foreach (int id in Enumerable.Range(1, world.BuildingSlots))
            {
                Building b = world.Building(id);
                if (b.Tribe != 0 || b.HitPoints <= 0 || b.Kind > 11 || b.Kind == 6
                    || b.X < seen.Column || b.X >= seen.Column + seen.Columns || b.Y < seen.Row || b.Y >= seen.Row + seen.Rows)
                {
                    continue;
                }
                Assert.True(Squares(world, view, new HashSet<int> { id }) > 0, $"{snapshot}: building {id} (kind {b.Kind}) pointed at shows no place squares");
                checkedBuildings++;
            }
            Assert.Equal(0, Squares(world, view, new HashSet<int>()));
        }
        Assert.True(checkedBuildings > 0, "no snapshot shows one of the player's buildings");
    }
}
