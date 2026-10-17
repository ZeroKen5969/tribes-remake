using System;

namespace Tribes.Client.Ui.Logic;

/// <summary>Room kept free along each edge of the screen.</summary>
/// <param name="Left">Room on the left.</param>
/// <param name="Top">Room at the top.</param>
/// <param name="Right">Room on the right.</param>
/// <param name="Bottom">Room at the bottom.</param>
public readonly record struct EdgeMargins(float Left, float Top, float Right, float Bottom)
{
    /// <summary>No room kept.</summary>
    public static EdgeMargins None => default;

    /// <summary>The larger room of the two on each edge.</summary>
    public EdgeMargins Max(EdgeMargins other) =>
        new(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    /// <summary><paramref name="extra"/> more on every edge.</summary>
    public EdgeMargins Plus(float extra) => new(Left + extra, Top + extra, Right + extra, Bottom + extra);

    /// <summary>The room in another unit: every edge times <paramref name="factor"/>, rounded up to whole units.</summary>
    public EdgeMargins ScaledUp(float factor) => new(Up(Left * factor), Up(Top * factor), Up(Right * factor), Up(Bottom * factor));

    // A thousandth below a whole number counts as that number (30 window pixels at scale 3 are 10, not 11).
    private static float Up(float value) => MathF.Ceiling(value - 0.001f);
}

/// <summary>A corner of the screen: the radius and centre of its rounding, in window pixels; radius 0 for a square corner.</summary>
/// <param name="Radius">Radius of the rounding.</param>
/// <param name="CentreX">The rounding circle's centre, from the window's left edge.</param>
/// <param name="CentreY">The rounding circle's centre, from the window's top edge.</param>
public readonly record struct ScreenCorner(float Radius, float CentreX, float CentreY)
{
    /// <summary>A square corner.</summary>
    public static ScreenCorner Square => default;

    /// <summary>The corners (top-left, top-right, bottom-right, bottom-left) of a <paramref name="width"/> × <paramref name="height"/> window all rounded by <paramref name="radius"/>.</summary>
    public static ScreenCorner[] AllRounded(float width, float height, float radius) =>
    [
        new(radius, radius, radius),
        new(radius, width - radius, radius),
        new(radius, width - radius, height - radius),
        new(radius, radius, height - radius),
    ];
}

/// <summary>
/// How much room the interface keeps from the screen's edges, so nothing sits under a camera cut
/// out of the screen or in a rounded corner where a finger cannot reach it: the cut-outs' room,
/// the room a button in each corner needs to lie inside the rounding, and the room the player
/// asks for on top (the setting). Screens without either keep only the player's room.
/// </summary>
public static class SafeMargins
{
    /// <summary>The choices of extra room in the settings, in logical pixels (none, small, medium, large, larger).</summary>
    public static readonly int[] PlayerChoices = [0, 6, 12, 18, 24];

    /// <summary>The choice in <see cref="PlayerChoices"/> nearest to <paramref name="pixels"/>.</summary>
    public static int ChoiceFor(int pixels)
    {
        int best = 0;
        for (int i = 1; i < PlayerChoices.Length; i++)
        {
            if (Math.Abs(PlayerChoices[i] - pixels) < Math.Abs(PlayerChoices[best] - pixels))
            {
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// The room on each edge outside the safe area (<paramref name="safeX"/>, <paramref name="safeY"/>,
    /// <paramref name="safeWidth"/> × <paramref name="safeHeight"/>) of a <paramref name="width"/> × <paramref name="height"/> window.
    /// </summary>
    public static EdgeMargins OutsideSafeArea(float width, float height, float safeX, float safeY, float safeWidth, float safeHeight)
    {
        if (safeWidth <= 0 || safeHeight <= 0)
        {
            return EdgeMargins.None;
        }
        return new EdgeMargins(
            Math.Clamp(safeX, 0, width / 2),
            Math.Clamp(safeY, 0, height / 2),
            Math.Clamp(width - safeX - safeWidth, 0, width / 2),
            Math.Clamp(height - safeY - safeHeight, 0, height / 2));
    }

    /// <summary>
    /// The room each edge of a <paramref name="width"/> × <paramref name="height"/> window needs
    /// for its rounded corners (top-left, top-right, bottom-right, bottom-left): a rectangle kept
    /// that far from both edges of a corner has its own corner inside the rounding.
    /// </summary>
    public static EdgeMargins ForCorners(float width, float height, ReadOnlySpan<ScreenCorner> corners)
    {
        if (corners.Length != 4)
        {
            throw new ArgumentException("four corners are needed: top-left, top-right, bottom-right, bottom-left", nameof(corners));
        }
        float topLeft = Inset(corners[0].Radius, corners[0].CentreX, corners[0].CentreY);
        float topRight = Inset(corners[1].Radius, width - corners[1].CentreX, corners[1].CentreY);
        float bottomRight = Inset(corners[2].Radius, width - corners[2].CentreX, height - corners[2].CentreY);
        float bottomLeft = Inset(corners[3].Radius, corners[3].CentreX, height - corners[3].CentreY);
        return new EdgeMargins(
            Math.Max(topLeft, bottomLeft),
            Math.Max(topLeft, topRight),
            Math.Max(topRight, bottomRight),
            Math.Max(bottomLeft, bottomRight));
    }

    /// <summary>
    /// How far from both edges a rectangle's corner must be to lie inside a rounding of
    /// <paramref name="radius"/> whose centre is <paramref name="fromSide"/> from the side edge and
    /// <paramref name="fromEnd"/> from the top or bottom edge (about 0.29 of the radius for a plain
    /// rounding).
    /// </summary>
    public static float Inset(float radius, float fromSide, float fromEnd)
    {
        if (radius <= 0 || fromSide <= 0 || fromEnd <= 0)
        {
            return 0;
        }
        // The corner point (d, d) is on the circle: (fromSide - d)² + (fromEnd - d)² = radius²; the smaller root.
        float sum = fromSide + fromEnd;
        float discriminant = (sum * sum) - (2 * ((fromSide * fromSide) + (fromEnd * fromEnd) - (radius * radius)));
        float nearest = Math.Min(fromSide, fromEnd);
        if (discriminant < 0)
        {
            // The rounding does not cross the corner's diagonal: past the centre on one axis is inside.
            return nearest;
        }
        return Math.Clamp((sum - MathF.Sqrt(discriminant)) / 2, 0, nearest);
    }

    /// <summary>
    /// The room the interface keeps, in logical pixels: the larger of the cut-outs' and the
    /// corners' room (window pixels, <paramref name="pixelScale"/> window pixels to a logical one)
    /// plus the player's <paramref name="extra"/> logical pixels.
    /// </summary>
    public static EdgeMargins Combine(EdgeMargins cutouts, EdgeMargins corners, float pixelScale, int extra)
    {
        float scale = Math.Max(1, pixelScale);
        return cutouts.Max(corners).ScaledUp(1 / scale).Plus(Math.Max(0, extra));
    }
}
