using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.World;
using Tribes.Graphics.Composition;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The pictures of the match interface: the original's own icons (resource pieces, cost icons,
/// weapon and item icons, the action bar's effect icons, faces, building pictures) as textures,
/// and the few icons the original lacks, painted in its palette. Everything is cached.
/// </summary>
public static class HudArt
{
    /// <summary>Pieces of the resource bar: population, food, wood, stones, bones.</summary>
    public const int PopulationPiece = 1500, FoodPiece = 1502, WoodPiece = 1503, StonesPiece = 1504, BonesPiece = 1505;

    /// <summary>First face picture (720 + Unit.Portrait).</summary>
    public const int FirstFace = 720;

    /// <summary>First icon-bar item (659 + weapon or trap product 0..8); 668 is empty.</summary>
    public const int FirstBarItem = 659;

    /// <summary>First carried-item icon (625 + item).</summary>
    public const int FirstCarriedItem = 625;

    /// <summary>First cost icon (627 wood, 628 stones, 629 bones).</summary>
    public const int FirstCostIcon = 627;

    /// <summary>First mood face (675 + happiness / 21; 675 sad .. 679 happy).</summary>
    public const int FirstMoodFace = 675;

    /// <summary>Gender icons (619 male, 620 female).</summary>
    public const int MaleIcon = 619;

    /// <summary>First inline icon (560 + control code 0..22, also the skill icons).</summary>
    public const int FirstInlineIcon = 560;

    /// <summary>First building picture of the build menu (680 + kind).</summary>
    public const int FirstBuildingPicture = 680;

    /// <summary>The soft key "go on" mark of the story screens.</summary>
    public const int ContinueSprite = 532;

    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);

    /// <summary>Original sprite <paramref name="sprite"/>.</summary>
    public static Texture2D Sprite(int sprite) => Get("s" + sprite, () => Paint(OriginalArt.Shared.Composers.Sprites.ComposeSprite(sprite).Parts));

    /// <summary>Original piece <paramref name="piece"/>.</summary>
    public static Texture2D Piece(int piece) => Get("p" + piece, () => Paint(OriginalArt.Shared.Composers.Sprites.ComposePiece(piece).Parts));

    /// <summary>Frame <paramref name="frame"/> of original effect <paramref name="effect"/> (the action bar's icons).</summary>
    public static Texture2D Effect(int effect, int frame = 0) =>
        Get($"e{effect}.{frame}", () => Paint(OriginalArt.Shared.Composers.Sprites.ComposeEffect(effect, frame).Parts));

    /// <summary>The face picture <paramref name="portrait"/> with its skin in tribe colour <paramref name="colour"/>.</summary>
    public static Texture2D Face(int portrait, int colour) =>
        Get($"f{portrait}.{colour}", () =>
        {
            Image image = OriginalArt.Shared.Rasterize(OriginalArt.Shared.Composers.Sprites.ComposeSprite(FirstFace + Math.Max(0, portrait)).Parts, out _);
            FaceColours.Recolour(image, colour, Math.Max(0, portrait));
            return ImageTexture.CreateFromImage(image);
        });

    /// <summary>
    /// The picture of building kind <paramref name="kind"/> for the build menu and the building
    /// panel: the original's close-up of the building on grass (sprite 680 + kind). The original
    /// never shows one of the chief's hut (its picture is bare grass), so the chief's hut gets a
    /// close-up of its tent as the map draws it; the alliance building has its own, drawn in the
    /// original's style.
    /// </summary>
    public static Texture2D BuildingPicture(int kind) =>
        Tribes.Core.Systems.Endless.AllianceBuilding.Is(kind) ? AlliancePicture()
        : kind == Logic.RuleNumbers.ChiefsHut ? ChiefsHutPicture()
        : Sprite(FirstBuildingPicture + kind);

    /// <summary>Whether building kind <paramref name="kind"/> has a <see cref="BuildingPicture"/>.</summary>
    public static bool HasBuildingPicture(int kind) =>
        Logic.RuleNumbers.IsPlanned(kind) || Tribes.Core.Systems.Endless.AllianceBuilding.Is(kind);

    /// <summary>Where the alliance building's build-menu picture is (44 x 48, like the original's pictures).</summary>
    public const string AlliancePicturePath = "res://assets/endless/alliance-hall-picture.png";

    private static Texture2D AlliancePicture() => Get("alliance", () =>
        ResourceLoader.Exists(AlliancePicturePath) ? GD.Load<Texture2D>(AlliancePicturePath) : Sprite(FirstBuildingPicture + 6));

    /// <summary>The part of the chief's hut its picture shows, in pixels from its top-left cell: the tent and its tusks.</summary>
    private static readonly Rect2I ChiefsHutWindow = new(2, -18, 44, 48);

    /// <summary>The terrain tile under the close-ups: plain grass, as under the original's pictures.</summary>
    private const int GrassTile = 0;

    /// <summary>Building slot whose flag the close-ups show (the player's).</summary>
    private const int OwnSlot = 1;

    private static Texture2D ChiefsHutPicture() => Get("chiefs-hut", () => MapCloseUp(Logic.RuleNumbers.ChiefsHut, ChiefsHutWindow));

    /// <summary>
    /// Building kind <paramref name="kind"/> at level 1, the player's, drawn as the map draws it
    /// (ground of every cell of its footprint, then the building) on grass, cut to
    /// <paramref name="window"/> (pixels from its top-left cell).
    /// </summary>
    private static Texture2D MapCloseUp(int kind, Rect2I window)
    {
        Composers composers = OriginalArt.Shared.Composers;
        Tribes.Core.Content.Rules.BuildingKind footprint = App.GameApp.Instance.Content.Rules.Buildings.Kinds[kind];
        var parts = new List<DrawPart>();
        for (int y = Mathf.FloorToInt(window.Position.Y / (float)WorldView.CellHeight); y * WorldView.CellHeight < window.End.Y; y++)
        {
            for (int x = Mathf.FloorToInt(window.Position.X / (float)WorldView.CellWidth); x * WorldView.CellWidth < window.End.X; x++)
            {
                foreach (DrawPart part in composers.Terrain.ComposeTile(GrassTile).Parts)
                {
                    parts.Add(part with { X = part.X + (x * WorldView.CellWidth), Y = part.Y + (y * WorldView.CellHeight) });
                }
            }
        }
        for (int cell = 0; cell < footprint.Width * footprint.Height; cell++)
        {
            parts.AddRange(composers.Objects.Compose(ObjectRequest.Building(kind, 1, OwnSlot, ObjectPass.Ground) with { CellIndex = cell }).Drawing.Parts);
        }
        parts.AddRange(composers.Objects.Compose(ObjectRequest.Building(kind, 1, OwnSlot, ObjectPass.Upper)).Drawing.Parts);
        Image image = Image.CreateEmpty(window.Size.X, window.Size.Y, false, Image.Format.Rgba8);
        OriginalArt.Shared.Paint(image, parts, -window.Position);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>The icon of weapon or trap product <paramref name="product"/> (0..8).</summary>
    public static Texture2D BarItem(int product) => Sprite(FirstBarItem + product);

    /// <summary>The icon of carried item <paramref name="item"/>.</summary>
    public static Texture2D CarriedItem(int item) => Sprite(FirstCarriedItem + item);

    /// <summary>The cost icon of material <paramref name="material"/> (0 wood, 1 stones, 2 bones).</summary>
    public static Texture2D Cost(int material) => Sprite(FirstCostIcon + material);

    /// <summary>The mood face for <paramref name="happiness"/> (0..99).</summary>
    public static Texture2D Mood(int happiness) => Sprite(FirstMoodFace + Math.Clamp(happiness / 21, 0, 4));

    /// <summary>The inline icon of control code <paramref name="code"/> (0..22): tasks, skills, combat marks.</summary>
    public static Texture2D Inline(int code) => Sprite(FirstInlineIcon + code);

    /// <summary>A child as the original's unit bar draws it: the still picture of the children's animation.</summary>
    public static Texture2D ChildFigure() => Get("child", () =>
    {
        var request = new BodyRequest(BodySubject.Villager, ChildStillAnimation, 0, 0)
        {
            Portrait = true,
            Appearance = VillagerAppearance.Default with { Age = ChildFigureAge },
        };
        return Paint(OriginalArt.Shared.Composers.Bodies.Compose(request).Drawing.Parts);
    });

    /// <summary>The children's standing animation, whose still picture is a child's portrait.</summary>
    private const int ChildStillAnimation = 14;

    /// <summary>An age that draws a child.</summary>
    private const int ChildFigureAge = 6;

    /// <summary><paramref name="texture"/> in grey and paler, for something waiting (cached as <paramref name="key"/>).</summary>
    public static Texture2D Greyed(string key, Texture2D texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return Get("grey." + key, () =>
        {
            Image image = texture.GetImage();
            image.Convert(Image.Format.Rgba8);
            for (int y = 0; y < image.GetHeight(); y++)
            {
                for (int x = 0; x < image.GetWidth(); x++)
                {
                    Color c = image.GetPixel(x, y);
                    float grey = (c.R * 0.3f) + (c.G * 0.59f) + (c.B * 0.11f);
                    image.SetPixel(x, y, new Color(grey, grey, grey, c.A * 0.6f));
                }
            }
            return ImageTexture.CreateFromImage(image);
        });
    }

    /// <summary>A painted icon the original lacks.</summary>
    public static Texture2D Painted(PaintedIcon icon) => Get("i" + icon, () => ImageTexture.CreateFromImage(PaintedIcons.Paint(icon)));

    /// <summary>A texture rectangle showing <paramref name="texture"/> at whole-number <paramref name="scale"/>.</summary>
    public static TextureRect Icon(Texture2D texture, int scale = 1)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            CustomMinimumSize = texture.GetSize() * Math.Max(1, scale),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }

    private static Texture2D Paint(IReadOnlyList<DrawPart> parts) =>
        ImageTexture.CreateFromImage(OriginalArt.Shared.Rasterize(parts, out _));

    private static Texture2D Get(string key, Func<Texture2D> make)
    {
        if (!Cache.TryGetValue(key, out Texture2D? texture))
        {
            texture = make();
            Cache[key] = texture;
        }
        return texture;
    }
}
