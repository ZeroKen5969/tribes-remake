using System;
using System.Collections.Generic;
using Tribes.Core.Systems.Endless;
using Tribes.Core.World;
using Tribes.Graphics.Imaging;

namespace Tribes.Client.World.Scene;

/// <summary>
/// The alliance hall of endless worlds (building kind 26, 3 x 3 cells). The original has no
/// picture for it, so its picture comes from its own sheet (<see cref="AllianceHallArt"/>); while it
/// is being built it is the shared 3 x 3 building site, and the original's animated flag and repair
/// dust are drawn over it like over the other buildings.
/// </summary>
public static class AllianceHall
{
    /// <summary>Its building kind.</summary>
    public const int Kind = AllianceBuilding.Kind;

    /// <summary>Id of its picture sheet.</summary>
    public const string SheetId = AllianceBuilding.SpriteId;

    /// <summary>Picture frames: standing, damaged (below half its hit points), ruin (destroyed, until its dust settles).</summary>
    public const int Built = 0, Damaged = 1, Ruin = 2;

    /// <summary>Whether <paramref name="kind"/> is the alliance hall.</summary>
    public static bool Is(int kind) => kind == Kind;

    /// <summary>The frame showing <paramref name="building"/>.</summary>
    public static int FrameOf(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        return FrameOf(building.HitPoints, building.MaxHitPoints);
    }

    /// <summary>The frame for <paramref name="hitPoints"/> of <paramref name="maxHitPoints"/> (a ruin has negative hit points).</summary>
    public static int FrameOf(int hitPoints, int maxHitPoints) =>
        hitPoints < 0 ? Ruin : hitPoints * 2 < maxHitPoints ? Damaged : Built;
}

/// <summary>
/// The alliance hall's picture sheet: <see cref="Frames"/> frames side by side, each
/// <see cref="FrameWidth"/> x <see cref="FrameHeight"/> pixels with the building's top-left cell
/// at (<see cref="AnchorX"/>, <see cref="AnchorY"/>); where the flag flies and where the dust of a
/// repair rises, relative to the top-left cell.
/// </summary>
/// <param name="Sheet">The frames.</param>
/// <param name="FrameWidth">Width of a frame.</param>
/// <param name="FrameHeight">Height of a frame.</param>
/// <param name="AnchorX">Left of the building's top-left cell in a frame.</param>
/// <param name="AnchorY">Top of the building's top-left cell in a frame.</param>
/// <param name="Flag">Where the original's flag animation is drawn.</param>
/// <param name="DustSpots">Where the original's building dust is drawn while it is repaired.</param>
public sealed record AllianceHallArt(RgbaImage Sheet, int FrameWidth, int FrameHeight, int AnchorX, int AnchorY,
    (int X, int Y) Flag, IReadOnlyList<(int X, int Y)> DustSpots)
{
    /// <summary>Frames a sheet holds: standing, damaged, ruin.</summary>
    public const int Frames = 3;

    /// <summary>Size of a frame: the 3 x 3 cells and the roof above them.</summary>
    public const int StandardWidth = 66, StandardHeight = 72;

    private const int Roof = StandardHeight - 48;

    /// <summary>Dust spots of the original's 3 x 3 buildings (the store's).</summary>
    public static readonly IReadOnlyList<(int X, int Y)> StandardDust = new[] { (2, 37), (63, 36), (61, -5), (1, -5) };

    /// <summary>The flag on the standard sheet: on top of the post at the back left.</summary>
    public static readonly (int X, int Y) StandardFlag = (4, -19);

    /// <summary>The art for a standard sheet (three 66 x 72 frames, cells bottom-aligned).</summary>
    public static AllianceHallArt FromSheet(RgbaImage sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (sheet.Width < StandardWidth * Frames || sheet.Height < StandardHeight)
        {
            throw new ArgumentException($"the alliance hall sheet is {sheet.Width}x{sheet.Height}, not {StandardWidth * Frames}x{StandardHeight}", nameof(sheet));
        }
        return new AllianceHallArt(sheet, StandardWidth, StandardHeight, 0, Roof, StandardFlag, StandardDust);
    }

    /// <summary>A plain picture in the original's colours, used while the drawn sheet is missing.</summary>
    public static AllianceHallArt Placeholder() => FromSheet(PlaceholderSheet());

    /// <summary>The sheet's file under the client's assets folder.</summary>
    public const string AssetFile = "endless/alliance-hall.png";

    /// <summary>The art from the PNG sheet at <paramref name="path"/>, or the stand-in when there is no such file (tools, tests).</summary>
    public static AllianceHallArt FromFileOrPlaceholder(string path) =>
        System.IO.File.Exists(path) ? FromSheet(Png.Decode(System.IO.File.ReadAllBytes(path))) : Placeholder();

    private static RgbaImage PlaceholderSheet()
    {
        var sheet = new RgbaImage(StandardWidth * Frames, StandardHeight);
        sheet.Clear(0);
        for (int frame = 0; frame < Frames; frame++)
        {
            var paint = new Painter(sheet, frame * StandardWidth);
            if (frame == AllianceHall.Ruin)
            {
                paint.Ruin();
            }
            else
            {
                paint.Hall(frame == AllianceHall.Damaged);
            }
        }
        return sheet;
    }

    /// <summary>Draws the placeholder frames with a few shapes in the original's browns.</summary>
    private readonly struct Painter
    {
        private const int Shadow = 0x3a2814, Log = 0x8c5f30, LogDark = 0x5e3d1c, Door = 0x2a1a0c;
        private const int Thatch = 0xc8a050, ThatchDark = 0xa07830, Pole = 0x6b4a26, Band = 0xb03020, Char = 0x241608;
        private readonly RgbaImage sheet;
        private readonly int left;

        public Painter(RgbaImage sheet, int left)
        {
            this.sheet = sheet;
            this.left = left;
        }

        public void Hall(bool damaged)
        {
            Fill(6, 62, 54, 6, Shadow);
            Fill(8, 40, 50, 24, Log);
            for (int x = 10; x < 58; x += 4)
            {
                Fill(x, 40, 1, 24, LogDark);
            }
            Fill(29, 50, 8, 14, Door);
            for (int row = 0; row < 28; row++)
            {
                int half = 2 + (row * 31 / 27);
                Fill(33 - half, 14 + row, half * 2, 1, (row % 4 == 3) ? ThatchDark : Thatch);
            }
            Poles(damaged ? 20 : 2);
            if (damaged)
            {
                Fill(16, 26, 7, 5, Door);
                Fill(40, 32, 9, 4, Door);
                Fill(44, 46, 2, 12, Door);
                Fill(13, 52, 1, 9, Door);
            }
        }

        public void Ruin()
        {
            Fill(4, 58, 58, 10, Shadow);
            Fill(8, 54, 22, 4, LogDark);
            Fill(22, 50, 26, 4, Log);
            Fill(36, 56, 24, 4, LogDark);
            Fill(14, 60, 30, 3, Char);
            Fill(3, 44, 4, 20, Pole);
            Fill(59, 52, 4, 12, Char);
        }

        private void Poles(int leftTop)
        {
            Fill(3, leftTop, 4, 62 - leftTop, Pole);
            Fill(59, 2, 4, 60, Pole);
            for (int y = 8; y < 60; y += 12)
            {
                if (y >= leftTop)
                {
                    Fill(3, y, 4, 2, Band);
                }
                Fill(59, y, 4, 2, Band);
            }
        }

        private void Fill(int x, int y, int width, int height, int rgb) =>
            PartRasterizer.FillRect(sheet, left + x, y, width, height, unchecked((int)0xFF000000) | rgb);
    }
}
