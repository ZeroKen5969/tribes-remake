using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.World.Scene;
using Tribes.Graphics.Composition;

namespace Tribes.Client.World;

/// <summary>
/// Drawing the composed object layer: every part from the one atlas texture. The parts are cut into
/// runs in their order, each run a canvas item of its own: the parts of one moving unit, animal or
/// projectile make one run, the parts between them another. The runs are made once per
/// composition; between rule frames smooth motion only moves the runs of the movers, so the drawing
/// commands are not made again every frame.
/// </summary>
public partial class WorldView
{
    private readonly List<Rid> runs = new();
    private readonly List<(MoverKind Mover, int Id)> runMovers = new();
    private int runsUsed;
    private Rid runsAtlas;

    // The texture the runs draw from, kept alive while they use it.
    private Texture2D? runsTexture;

    /// <summary>Canvas items holding the object layer's runs now.</summary>
    public int ObjectRuns => runsUsed;

    /// <summary>Makes the runs of the last composition again (after a composition or a change of colours).</summary>
    private void BuildRuns()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        runsTexture = art.Atlas;
        Rid atlasRid = runsTexture.GetRid();
        runsAtlas = atlasRid;
        Rect2 white = art.WhitePixel;
        int originX = composedView.Column * CellWidth, originY = composedView.Row * CellHeight;
        runsUsed = 0;
        Rid run = default;
        (MoverKind Mover, int Id) current = (MoverKind.None, -1);
        string? lastSheet = null;
        int lastSkin = int.MinValue;
        Rect2I region = default;
        for (int i = 0; i < scene.Count; i++)
        {
            SceneItem item = scene[i];
            (MoverKind, int) key = (item.Mover, item.Mover == MoverKind.None ? 0 : item.MoverId);
            if (runsUsed == 0 || key != current)
            {
                current = key;
                run = NextRun(key);
            }
            DrawPart part = item.Part;
            int x = originX + part.X, y = originY + part.Y;
            switch (part.Kind)
            {
                case DrawPartKind.Image:
                    if (!ReferenceEquals(part.Sheet, lastSheet) || item.SkinTribe != lastSkin)
                    {
                        region = item.SkinTribe >= 0
                            ? art.Region(part.Sheet!, Looks.ColourOf(item.SkinTribe), TribeLooks.OutfitOf(item.SkinTribe))
                            : art.Region(part.Sheet!);
                        lastSheet = part.Sheet;
                        lastSkin = item.SkinTribe;
                    }
                    RenderingServer.CanvasItemAddTextureRectRegion(run, new Rect2(x, y, part.Width, part.Height), atlasRid,
                        new Rect2(region.Position.X + part.SourceX, region.Position.Y + part.SourceY, part.Width, part.Height));
                    break;
                case DrawPartKind.Fill:
                    Fill(run, atlasRid, white, x, y, part.Width, part.Height, ToColor(part));
                    break;
                case DrawPartKind.Outline:
                    DrawOutline(run, atlasRid, white, x, y, part.Width, part.Height, ToColor(part));
                    break;
                case DrawPartKind.Line:
                    DrawLine(run, atlasRid, white, x, y, originX + part.EndX, originY + part.EndY, ToColor(part));
                    break;
                default:
                    break;
            }
        }
        for (int i = runsUsed; i < runs.Count; i++)
        {
            RenderingServer.CanvasItemClear(runs[i]);
        }
        DrawMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (art.Atlas.GetRid() != atlasRid)
        {
            // A recoloured sheet was added while the runs were made: make them again on the new atlas.
            BuildRuns();
            return;
        }
        MoveRuns();
    }

    /// <summary>The next run (cleared), drawn after the ones before it.</summary>
    private Rid NextRun((MoverKind Mover, int Id) mover)
    {
        Rid run;
        if (runsUsed < runs.Count)
        {
            run = runs[runsUsed];
            RenderingServer.CanvasItemClear(run);
            runMovers[runsUsed] = mover;
        }
        else
        {
            run = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(run, objectCanvas.GetCanvasItem());
            RenderingServer.CanvasItemSetDefaultTextureFilter(run, RenderingServer.CanvasItemTextureFilter.Nearest);
            RenderingServer.CanvasItemSetDrawIndex(run, runs.Count);
            runs.Add(run);
            runMovers.Add(mover);
        }
        RenderingServer.CanvasItemSetTransform(run, Transform2D.Identity);
        runsUsed++;
        return run;
    }

    /// <summary>Moves the movers' runs to where smooth motion shows them now.</summary>
    private void MoveRuns()
    {
        if (!SmoothMotion)
        {
            return;
        }
        float progress = RuleFrameSeconds > 0 ? (float)(sinceRuleFrame / RuleFrameSeconds) : 1f;
        for (int i = 0; i < runsUsed; i++)
        {
            (MoverKind mover, int id) = runMovers[i];
            if (mover == MoverKind.None)
            {
                continue;
            }
            (int dx, int dy) = motion.Offset(mover, id, progress);
            RenderingServer.CanvasItemSetTransform(runs[i], new Transform2D(0, new Vector2(dx, dy)));
        }
    }

    /// <summary>Frees the runs (the view leaves the tree).</summary>
    private void FreeRuns()
    {
        foreach (Rid run in runs)
        {
            RenderingServer.FreeRid(run);
        }
        runs.Clear();
        runMovers.Clear();
        runsUsed = 0;
        runsTexture = null;
        // Back in the tree, the next frame composes and makes the runs again.
        sceneDirty = true;
    }

    /// <summary>Whether the atlas texture was replaced since the runs were made (they must be made again).</summary>
    private bool AtlasReplaced => runsUsed > 0 && art.Atlas.GetRid() != runsAtlas;

    /// <summary>A filled rectangle, drawn as the atlas' white pixel tinted (so it batches with the pictures).</summary>
    private static void Fill(Rid run, Rid atlas, Rect2 white, int x, int y, int width, int height, Color color)
    {
        if (width > 0 && height > 0)
        {
            RenderingServer.CanvasItemAddTextureRectRegion(run, new Rect2(x, y, width, height), atlas, white, color);
        }
    }

    private static Color ToColor(DrawPart part) =>
        part.Color is { } c ? Color.Color8((byte)c.R, (byte)c.G, (byte)c.B) : Colors.Black;

    /// <summary>An outline covering width + 1 by height + 1 pixels, as the original's drawRect.</summary>
    private static void DrawOutline(Rid run, Rid atlas, Rect2 white, int x, int y, int width, int height, Color color)
    {
        if (width < 0 || height < 0)
        {
            return;
        }
        if (width == 0 || height == 0)
        {
            DrawLine(run, atlas, white, x, y, x + width, y + height, color);
            return;
        }
        Fill(run, atlas, white, x, y, width + 1, 1, color);
        Fill(run, atlas, white, x, y + height, width + 1, 1, color);
        Fill(run, atlas, white, x, y + 1, 1, height - 1, color);
        Fill(run, atlas, white, x + width, y + 1, 1, height - 1, color);
    }

    /// <summary>A one-pixel line with both ends, the same pixels as the terrain rasteriser draws.</summary>
    private static void DrawLine(Rid run, Rid atlas, Rect2 white, int x1, int y1, int x2, int y2, Color color)
    {
        if (y1 == y2)
        {
            Fill(run, atlas, white, Math.Min(x1, x2), y1, Math.Abs(x2 - x1) + 1, 1, color);
            return;
        }
        if (x1 == x2)
        {
            Fill(run, atlas, white, x1, Math.Min(y1, y2), 1, Math.Abs(y2 - y1) + 1, color);
            return;
        }
        int dx = Math.Abs(x2 - x1), dy = -Math.Abs(y2 - y1);
        int sx = x1 < x2 ? 1 : -1, sy = y1 < y2 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            Fill(run, atlas, white, x1, y1, 1, 1, color);
            if (x1 == x2 && y1 == y2)
            {
                return;
            }
            int doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x1 += sx;
            }
            if (doubled <= dx)
            {
                error += dx;
                y1 += sy;
            }
        }
    }
}
