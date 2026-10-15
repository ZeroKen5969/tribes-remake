using System;
using System.Collections.Generic;
using System.IO;
using Tribes.Client.World.Scene;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;

namespace Tribes.Client.MatchPlay.Tests;

/// <summary>The repository's rules content and the original art, loaded once for all tests.</summary>
internal sealed class Repo
{
    private static readonly Lazy<Repo> Shared = new(() => new Repo());
    private readonly Dictionary<string, RgbaImage> sheets = new(StringComparer.Ordinal);

    private Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data")) && Directory.Exists(Path.Combine(dir.FullName, "data", "assets")))
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
        foreach (SheetInfo sheet in Catalog.Sheets)
        {
            sheets[sheet.Id] = Png.Decode(File.ReadAllBytes(Path.Combine(Root, "data", "assets", sheet.File)));
        }
    }

    public static Repo Instance => Shared.Value;

    public string Root { get; } = string.Empty;

    public GameContent Content { get; }

    public SpriteCatalog Catalog { get; }

    public SceneAssets Assets { get; }

    public RgbaImage Sheet(string id) => sheets[id];

    /// <summary>Mission <paramref name="mission"/> played past its opening scenes until the world runs (at most <paramref name="ticks"/> ticks).</summary>
    public GameSimulation Started(int mission, int ticks = 400)
    {
        GameSimulation game = GameSimulation.StartMission(Content, mission, 0);
        for (int i = 0; i < ticks && (game.NextKind != TickKind.Play || game.World.Frame < 5); i++)
        {
            game.Step(game.CanContinueScene ? TickInput.WithOrders(new AdvanceScene(0)) : TickInput.Idle);
        }
        return game;
    }
}
