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

namespace Tribes.Client.World.Tests;

/// <summary>
/// The repository's data and assets, loaded once, and the original repository's dev snapshots
/// (original/reference/snapshots: worlds of the original's maps, used only by the tests in Original/).
/// </summary>
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

    /// <summary>A path in the original repository's reference data (original/reference).</summary>
    public string OriginalReference(params string[] parts) => Path.Combine(Root, "original", "reference", Path.Combine(parts));

    public IEnumerable<string> Snapshots() =>
        Directory.GetFiles(OriginalReference("snapshots"), "*.trbw").OrderBy(f => f, StringComparer.Ordinal);

    public (GameWorld World, SnapshotView View) Load(string snapshot)
    {
        using FileStream file = File.OpenRead(snapshot);
        return (WorldSnapshot.Load(file, Content), SnapshotView.Parse(File.ReadAllText(Path.ChangeExtension(snapshot, ".json"))));
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

/// <summary>What only the screen keeps (smoothing of a snapshot's units: Original/PresentationTests.Snapshots.cs).</summary>
public sealed partial class PresentationTests
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
}
