using System;

namespace Tribes.Client.MatchPlay.Logic;

/// <summary>How a row of square buttons fits along an edge: in how many lines, how many per line, and how big.</summary>
/// <param name="Lines">Lines of buttons (side by side across the edge).</param>
/// <param name="PerLine">Most buttons in one line.</param>
/// <param name="Button">Side of a button.</param>
public readonly record struct StripFit(int Lines, int PerLine, int Button)
{
    /// <summary>How deep the lines are across the edge, with <paramref name="gap"/> between lines.</summary>
    public int Thickness(int gap) => (Lines * Button) + (Math.Max(0, Lines - 1) * gap);
}

/// <summary>
/// Fits the big buttons of the match along a screen edge so every one is on the screen: in one
/// line at their full size when there is room; otherwise a little smaller, but never below the
/// smallest size a finger presses reliably; and when even that is too long, in two (or more)
/// lines at the size that then fits.
/// </summary>
public static class ButtonStrip
{
    /// <summary>
    /// The fit of <paramref name="count"/> buttons of <paramref name="preferred"/> pixels (at least
    /// <paramref name="smallest"/>) with <paramref name="gap"/> between them along <paramref name="length"/> pixels.
    /// </summary>
    public static StripFit Fit(int count, float length, int preferred, int smallest, int gap)
    {
        if (preferred <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preferred), preferred, "buttons need a size");
        }
        if (count <= 0)
        {
            return new StripFit(1, 0, preferred);
        }
        int least = Math.Clamp(smallest, 1, preferred);
        for (int lines = 1; lines <= count; lines++)
        {
            int perLine = (count + lines - 1) / lines;
            int size = Math.Min(preferred, Along(length, perLine, gap));
            if (size >= least)
            {
                return new StripFit(lines, perLine, size);
            }
        }
        // Not even one button of the smallest size fits: one per line, as big as the edge allows.
        return new StripFit(count, 1, Math.Clamp(Along(length, 1, gap), 1, preferred));
    }

    /// <summary>The largest side of <paramref name="perLine"/> buttons with gaps along <paramref name="length"/>.</summary>
    private static int Along(float length, int perLine, int gap) => (int)MathF.Floor((length - ((perLine - 1) * gap)) / perLine);
}
