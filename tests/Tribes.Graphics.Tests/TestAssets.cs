using System;
using System.Collections.Generic;
using System.IO;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;

namespace Tribes.Graphics.Tests;

/// <summary>Paths of the repository and of the exported assets.</summary>
internal static class TestPaths
{
    /// <summary>The repository root: the first directory above the tests with data/ and game/res.</summary>
    public static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data", "rules")) && File.Exists(Path.Combine(dir.FullName, "Tribes.sln")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("repository root not found above " + AppContext.BaseDirectory);
    }

    public static string Data(params string[] parts) => Path.Combine(RepoRoot(), "data", "client", Path.Combine(parts));

    public static string Assets(params string[] parts) => Path.Combine(RepoRoot(), "data", "assets", Path.Combine(parts));

    public static string Reference(params string[] parts) =>
        Path.Combine(RepoRoot(), "tests", "Tribes.Graphics.Tests", "Data", Path.Combine(parts));
}

/// <summary>The exported sprite catalog, its sheets and composers, loaded once.</summary>
internal sealed class TestAssets
{
    private static readonly Lazy<TestAssets> Shared = new(() => new TestAssets());

    private TestAssets()
    {
        Catalog = CatalogJson.Read<SpriteCatalog>(TestPaths.Data("sprites.json"));
        Composers = new Composers(Catalog);
        var sheets = new Dictionary<string, RgbaImage>(StringComparer.Ordinal);
        foreach (SheetInfo sheet in Catalog.Sheets)
        {
            sheets[sheet.Id] = Png.Decode(File.ReadAllBytes(TestPaths.Assets(sheet.File)));
        }
        Sheets = sheets;
        Rasterizer = new PartRasterizer(id => Sheets[id]);
    }

    public static TestAssets Instance => Shared.Value;

    public SpriteCatalog Catalog { get; }

    public Composers Composers { get; }

    public IReadOnlyDictionary<string, RgbaImage> Sheets { get; }

    public PartRasterizer Rasterizer { get; }
}
