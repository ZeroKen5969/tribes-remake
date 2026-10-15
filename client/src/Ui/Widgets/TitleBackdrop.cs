using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Graphics.Composition;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// The original title screen behind the menus, fitted to any screen shape: the camp scenery with
/// its three villagers (object graphic 118, see Tribes.Graphics <see cref="MenuScene"/>) stands on
/// the bottom edge, the forest from the original terrain tiles continues it to both sides, and all
/// the space above is sky with the original clouds drifting across. Everything is drawn at the
/// window's whole-number pixel scale.
/// </summary>
public partial class TitleBackdrop : Control
{
    private const int HillsSprite = 1666;
    private const int HillsAboveAnchor = 45;
    private const int SceneTop = -53;
    private const int SceneBottom = 54;
    private const int SceneWidth = 244;
    private const double FrameSeconds = 0.1;
    private const int CellWidth = 22;
    private const int CellHeight = 16;
    private const int WingTreeRows = 3;
    private const int GrassTile = 0;
    private const int GrassVariants = 3;
    private const int FirstTreeTile = 180;
    private const int TreeTileCount = 50;
    private const int CloudArea = 7000;
    private static readonly int[] CloudSprites = [1667, 1668, 1669, 1670];

    // From a deeper blue at the top to the original sky colour at the horizon (same hue), in steps.
    private static readonly Color SkyTop = new("#5fa9f0");
    private const int SkySteps = 8;
    private const int DitherRows = 6;
    private static readonly int[,] Bayer = { { 0, 2 }, { 3, 1 } };

    private readonly List<(int Sprite, float X, float Y, float Speed)> clouds = new();
    private readonly List<DrawPart> leftWing = new();
    private readonly List<DrawPart> rightWing = new();
    private int wingColumns = -1;
    private IReadOnlyList<DrawPart> scenery = Array.Empty<DrawPart>();
    private IReadOnlyList<DrawPart> villagers = Array.Empty<DrawPart>();
    private Texture2D? sky;
    private Vector2 laidOutFor = -Vector2.One;
    private double clock;
    private int tick;
    private float hillsWidth = 176;

    /// <summary>Where the scenery stands, as a fraction of the width (0.5 = centred).</summary>
    public float SceneCenter { get; set; } = 0.5f;

    /// <summary>
    /// Pixels of the scenery's lowest grass hidden below the bottom edge (the villagers stay well
    /// above it); larger values leave more sky on short screens.
    /// </summary>
    public int BottomCrop { get; set; } = 6;

    /// <summary>The scenery's rectangle in this control (for laying other things out around it).</summary>
    public Rect2 SceneRect
    {
        get
        {
            Vector2 anchor = SceneAnchor();
            return new Rect2(anchor.X, anchor.Y + SceneTop, SceneWidth, SceneBottom - SceneTop);
        }
    }

    /// <summary>The sky above the tree line: where logos and menus go without hiding the scenery.</summary>
    public Rect2 SkyRect => new(0, 0, Size.X, Math.Max(0, SceneRect.Position.Y));

    /// <inheritdoc/>
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        OriginalArt art = OriginalArt.Shared;
        scenery = art.Composers.Objects.Compose(MenuScene.Request(ObjectPass.Preview, 0)).Drawing.Parts;
        hillsWidth = Math.Max(1, OriginalArt.Bounds(art.Composers.Sprites.ComposeSprite(HillsSprite).Parts).Size.X);
        Compose();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        clock += delta;
        if (clock < FrameSeconds)
        {
            return;
        }
        clock -= FrameSeconds;
        tick++;
        for (int i = 0; i < clouds.Count; i++)
        {
            (int sprite, float x, float y, float speed) = clouds[i];
            x += speed;
            if (x > Size.X + 20)
            {
                x = -80;
            }
            clouds[i] = (sprite, x, y, speed);
        }
        Compose();
        QueueRedraw();
    }

    private void Compose() =>
        villagers = OriginalArt.Shared.Composers.Objects.Compose(MenuScene.Request(ObjectPass.Upper, tick)).Drawing.Parts;

    private Vector2 SceneAnchor() =>
        new(MathF.Round(Size.X * SceneCenter - SceneWidth / 2f), MathF.Round(Size.Y - SceneBottom + BottomCrop));

    /// <summary>Rebuilds the sky and the clouds when the size or the scenery's place changed.</summary>
    private void LayOut(Vector2 anchor)
    {
        Vector2 key = new(Size.X * 10000 + Size.Y, anchor.Y);
        if (key == laidOutFor || Size.X < 1 || Size.Y < 1)
        {
            return;
        }
        laidOutFor = key;
        float horizon = anchor.Y - HillsAboveAnchor + 12;
        sky = ImageTexture.CreateFromImage(PaintSky((int)Size.X, Math.Max(1, (int)horizon)));
        clouds.Clear();
        var random = new Random(7);
        int count = Math.Clamp((int)(Size.X * horizon / CloudArea), 4, 24);
        float lowest = Math.Max(8, horizon - 44);
        for (int i = 0; i < count; i++)
        {
            int kind = i % CloudSprites.Length;
            clouds.Add((CloudSprites[kind], random.Next(-80, (int)Size.X + 20), 4 + (float)random.NextDouble() * (lowest - 4), (kind + 2) / 2f));
        }
    }

    /// <summary>
    /// The sky in flat steps from deep blue down to the original horizon colour; each change of
    /// step is an ordered dither over a few rows, as pixel art blends colours.
    /// </summary>
    private static Image PaintSky(int width, int height)
    {
        var steps = new Color[SkySteps];
        for (int i = 0; i < SkySteps; i++)
        {
            steps[i] = SkyTop.Lerp(UiPalette.Sky, i / (float)(SkySteps - 1));
        }
        float stepHeight = height / (float)SkySteps;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            float position = y / stepHeight;
            int step = Math.Min(SkySteps - 1, (int)position);
            float into = (position - step) * stepHeight;
            float blend = step < SkySteps - 1 ? Math.Clamp((into - (stepHeight - DitherRows)) / DitherRows, 0, 1) : 0;
            for (int x = 0; x < width; x++)
            {
                bool next = blend > 0 && (Bayer[y % 2, x % 2] + 0.5f) / 4f < blend;
                Color c = steps[next ? step + 1 : step];
                int i = (y * width + x) * 4;
                pixels[i] = (byte)(c.R * 255);
                pixels[i + 1] = (byte)(c.G * 255);
                pixels[i + 2] = (byte)(c.B * 255);
                pixels[i + 3] = 255;
            }
        }
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);
    }

    /// <summary>Lays out enough forest cells to reach both screen edges (rebuilt when the width changes).</summary>
    private void ComposeWings(Vector2 anchor)
    {
        // A window without a size yet (or a headless run) gives no usable width: no wings then.
        double reach = Math.Max(anchor.X, Size.X - anchor.X - SceneWidth);
        int columns = double.IsFinite(reach) && reach > 0 ? (int)Math.Min(Math.Ceiling(reach / CellWidth) + 2, MaxWingColumns) : 0;
        if (columns == wingColumns)
        {
            return;
        }
        wingColumns = columns;
        ComposeWing(leftWing, columns, 11);
        ComposeWing(rightWing, columns, 29);
    }

    /// <summary>The most forest columns a wing gets (far wider than any screen).</summary>
    private const int MaxWingColumns = 400;

    private static void ComposeWing(List<DrawPart> parts, int columns, int seed)
    {
        parts.Clear();
        TerrainComposer terrain = OriginalArt.Shared.Composers.Terrain;
        var random = new Random(seed);
        int rows = WingTreeRows + 1;
        var tiles = new int[columns, rows];
        for (int column = 0; column < columns; column++)
        {
            for (int row = 0; row < rows; row++)
            {
                tiles[column, row] = row < WingTreeRows ? FirstTreeTile + random.Next(TreeTileCount) : GrassTile + random.Next(GrassVariants);
            }
        }
        parts.AddRange(terrain.ComposeView(new TerrainView(columns, rows, (x, y) => tiles[x, y], 0, 0)));
        // Object layer as the original draws it: rows from the top, each row from the right.
        for (int row = 0; row < rows; row++)
        {
            for (int column = columns - 1; column >= 0; column--)
            {
                foreach (DrawPart part in terrain.ComposeOverlay(tiles[column, row], 0, row).Parts)
                {
                    parts.Add(part.Offset(column * CellWidth, row * CellHeight));
                }
            }
        }
    }

    /// <summary>A row of the original grass tiles along the bottom edge, under the scenery's ragged lower border.</summary>
    private void DrawGrassStrip()
    {
        TerrainComposer terrain = OriginalArt.Shared.Composers.Terrain;
        int columns = (int)Math.Ceiling(Size.X / CellWidth) + 1;
        for (int column = 0; column < columns; column++)
        {
            int tile = GrassTile + (int)((uint)(column * 7919) % GrassVariants);
            OriginalArt.Shared.Draw(this, terrain.ComposeTile(tile).Parts, new Vector2(column * CellWidth, Size.Y - CellHeight));
        }
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        OriginalArt art = OriginalArt.Shared;
        Vector2 anchor = SceneAnchor();
        LayOut(anchor);
        DrawRect(new Rect2(Vector2.Zero, Size), UiPalette.Sky);
        if (sky is not null)
        {
            DrawTexture(sky, Vector2.Zero);
        }
        foreach ((int sprite, float x, float y, float _) in clouds)
        {
            art.DrawSprite(this, sprite, new Vector2(MathF.Round(x), y));
        }
        float hillsY = anchor.Y - HillsAboveAnchor;
        float start = anchor.X - 66;
        while (start > 0)
        {
            start -= hillsWidth;
        }
        for (float x = start; x < Size.X; x += hillsWidth)
        {
            art.DrawSprite(this, HillsSprite, new Vector2(x, hillsY));
        }
        // Behind the trees only: the hills' open lower edge continues in their darkest green.
        DrawRect(new Rect2(0, hillsY + 29, Size.X, Math.Max(0, Size.Y - hillsY - 29)), UiPalette.Ground);
        DrawGrassStrip();
        ComposeWings(anchor);
        art.Draw(this, leftWing, new Vector2(anchor.X + 10 - wingColumns * CellWidth, anchor.Y - 12));
        art.Draw(this, rightWing, new Vector2(anchor.X + SceneWidth - 12, anchor.Y - 12));
        art.Draw(this, scenery, anchor);
        art.Draw(this, villagers, anchor);
    }
}
