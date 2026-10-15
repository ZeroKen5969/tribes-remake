using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tribes.Client.World.Scene;
using Tribes.Core.Content;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Client.World.Tests;

/// <summary>The repository's data, assets and dev snapshots, loaded once.</summary>
internal sealed class Repo
{
    private static readonly Lazy<Repo> Shared = new(() => new Repo());
    private readonly Dictionary<string, RgbaImage> sheets = new(StringComparer.Ordinal);

    private Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data")) && Directory.Exists(Path.Combine(dir.FullName, "client", "dev")))
            {
                Root = dir.FullName;
                break;
            }
        }
        if (Root.Length == 0)
        {
            throw new DirectoryNotFoundException("repository root not found above " + AppContext.BaseDirectory);
        }
        Content = GameContent.Load(Path.Combine(Root, "data"));
        Catalog = CatalogJson.Read<SpriteCatalog>(Path.Combine(Root, "data", "client", "sprites.json"));
        var fonts = CatalogJson.Read<BitmapFontCatalog>(Path.Combine(Root, "data", "client", "fonts.json"));
        Assets = new SceneAssets(Catalog, new Composers(Catalog), fonts);
        Colours = TribeColours.Parse(File.ReadAllText(Path.Combine(Root, "data", "client", "tribe-colours.json")));
        foreach (SheetInfo sheet in Catalog.Sheets)
        {
            sheets[sheet.Id] = Png.Decode(File.ReadAllBytes(Path.Combine(Root, "data", "assets", sheet.File)));
        }
        Assets.AllianceHall = AllianceHallArt.FromFileOrPlaceholder(Path.Combine(Root, "data", "assets", AllianceHallArt.AssetFile));
        sheets[AllianceHall.SheetId] = Assets.AllianceHall.Sheet;
    }

    public static Repo Instance => Shared.Value;

    public string Root { get; } = string.Empty;

    public GameContent Content { get; }

    public SpriteCatalog Catalog { get; }

    public SceneAssets Assets { get; }

    public TribeColours Colours { get; }

    public RgbaImage Sheet(string id) => sheets[id];

    public string Dev(params string[] parts) => Path.Combine(Root, "client", "dev", Path.Combine(parts));

    public IEnumerable<string> Snapshots() =>
        Directory.GetFiles(Dev("snapshots"), "*.trbw").OrderBy(f => f, StringComparer.Ordinal);

    public (GameWorld World, SnapshotView View) Load(string snapshot)
    {
        using FileStream file = File.OpenRead(snapshot);
        return (WorldSnapshot.Load(file, Content), SnapshotView.Parse(File.ReadAllText(Path.ChangeExtension(snapshot, ".json"))));
    }
}

/// <summary>The world view's picture against the original's frames.</summary>
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
            string reference = repo.Dev("reference", $"s{view.Seed}", $"f{view.Frame:D5}.png");
            Assert.True(File.Exists(reference), "missing " + reference + " (run tools/Tribes.DevSnapshots/reference.sh)");
            RgbaImage screen = FrameCheck.DrawScreen(world, view, repo.Assets, repo.Sheet);
            FrameCheckResult result = FrameCheck.Measure(view, Png.Decode(File.ReadAllBytes(reference)), screen,
                FrameCheck.StaleCells(repo.Dev("reference", $"s{view.Seed}", "terrain.txt"), view.Frame),
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
}

/// <summary>Tribe colours and the skin recolouring.</summary>
public sealed class TribeColourTests
{
    [Fact]
    public void TheColourFileHasEightDistinctNamedColours()
    {
        TribeColours colours = Repo.Instance.Colours;
        Assert.Equal(TribeColours.Count, colours.Colours.Count);
        Assert.Equal(TribeColours.Count, colours.Colours.Select(c => c.Swatch).Distinct().Count());
        foreach (TribeColour colour in colours.Colours)
        {
            Assert.False(string.IsNullOrWhiteSpace(colour.Name));
            Assert.Equal(10, colour.Skin.Count);
            Assert.Equal(colour.Swatch, colour.Skin[0]);
        }
    }

    [Fact]
    public void TheOriginalTribesKeepTheirOwnPictures()
    {
        Repo repo = Repo.Instance;
        var skin = new SkinRecolour(repo.Colours, repo.Catalog, repo.Sheet);
        foreach (string sheet in skin.SkinSheets)
        {
            Assert.Null(skin.Recolour(sheet, 0, Tribe.Player));
            Assert.Null(skin.Recolour(sheet, 1, Tribe.Computer));
        }
        Assert.Null(skin.Recolour("pi0_00", 5, Tribe.Player));
    }

    [Fact]
    public void AnotherColourChangesTheSkinOnly()
    {
        Repo repo = Repo.Instance;
        var skin = new SkinRecolour(repo.Colours, repo.Catalog, repo.Sheet);
        RgbaImage limbs = repo.Sheet("pi0_14"), computerLimbs = repo.Sheet("pi9_04");
        RgbaImage indigo = skin.Recolour("pi0_14", 7, Tribe.Player)!;
        var ramp = repo.Colours.Colours[7].Skin.Select(c => RgbColor.Parse(c).Value).ToHashSet();
        int changed = 0;
        for (int y = 0; y < limbs.Height; y++)
        {
            for (int x = 0; x < limbs.Width; x++)
            {
                bool isSkin = (limbs[x, y] >>> 24) != 0 && (limbs[x, y] & 0xFFFFFF) != (computerLimbs[x, y] & 0xFFFFFF);
                if (isSkin)
                {
                    Assert.Contains(indigo[x, y] & 0xFFFFFF, ramp);
                    changed++;
                }
                else
                {
                    Assert.Equal(limbs[x, y], indigo[x, y]);
                }
            }
        }
        Assert.True(changed > 1000, $"only {changed} skin pixels");
    }

    [Fact]
    public void NoTwoTribesShareAColour()
    {
        var looks = new TribeLooks(0, 3);
        Assert.Equal(0, looks.ColourOf(0));
        Assert.Equal(1, looks.ColourOf(1));
        Assert.False(looks.TrySetColour(0, 1));
        Assert.True(looks.IsTaken(1, 0));
        Assert.True(looks.TrySetColour(0, 7));
        Assert.True(looks.TrySetColour(1, 0));
        Assert.False(looks.TrySetColour(2, 7));
        Assert.False(looks.TrySetColour(2, 8));
        Assert.Equal(new[] { 7, 0, 2 }, Enumerable.Range(0, 3).Select(looks.ColourOf));
        Assert.True(looks.TrySetColours(new[] { 0, 7, 2 }));
        Assert.Equal(new[] { 0, 7, 2 }, Enumerable.Range(0, 3).Select(looks.ColourOf));
        Assert.False(looks.TrySetColours(new[] { 3, 3, 4 }));
        Assert.Equal(new[] { 0, 7, 2 }, Enumerable.Range(0, 3).Select(looks.ColourOf));
    }

    [Fact]
    public void TheOutfitsFollowTheOriginalsTwoTribes()
    {
        Assert.Equal(Tribe.Player, TribeLooks.OutfitOf(0));
        Assert.Equal(Tribe.Computer, TribeLooks.OutfitOf(1));
    }
}

/// <summary>What only the screen keeps.</summary>
public sealed class PresentationTests
{
    [Fact]
    public void JavaRandomGivesJavasNumbers()
    {
        // new java.util.Random(42).nextInt() and the next value.
        var random = new JavaRandom(42);
        Assert.Equal(-1170105035, random.NextInt());
        Assert.Equal(234785527, random.NextInt());
    }

    [Fact]
    public void ChangesBecomeRisingNumbersEveryThirdFrame()
    {
        var labels = new FloatingLabels();
        labels.Add(4, -3);
        labels.Add(4, -2);
        labels.Step(2);
        Assert.Equal(0, labels.End);
        labels.Step(4);
        Assert.Equal(1, labels.End);
        Assert.Equal("-5", labels.Ring[0].Text);
        Assert.Equal(1, labels.Ring[0].Colour);
        int frames = 0;
        while (labels.First != labels.End)
        {
            labels.Step(5);
            frames++;
        }
        Assert.Equal(9, frames);
    }

    [Fact]
    public void ShortMovesAreSmoothedAndJumpsAreNot()
    {
        Repo repo = Repo.Instance;
        (GameWorld world, _) = repo.Load(repo.Snapshots().First());
        var motion = new MotionTracker();
        var presentation = new ScenePresentation();
        Unit unit = Enumerable.Range(1, world.UnitSlots).Select(world.Unit).First(u => u.InUse);
        motion.OnRuleFrame(world, presentation);
        unit.OffsetX += 4;
        motion.OnRuleFrame(world, presentation);
        Assert.Equal((-4, 0), motion.Offset(MoverKind.Unit, unit.Id, 0f));
        Assert.Equal((-2, 0), motion.Offset(MoverKind.Unit, unit.Id, 0.5f));
        Assert.Equal((0, 0), motion.Offset(MoverKind.Unit, unit.Id, 1f));
        unit.X += 5;
        motion.OnRuleFrame(world, presentation);
        Assert.Equal((0, 0), motion.Offset(MoverKind.Unit, unit.Id, 0f));
    }
}
