using System;
using System.Collections.Generic;
using Tribes.Graphics.Model;

namespace Tribes.Client.World.Scene;

/// <summary>
/// The skin of the original's face pictures (the portraits of the unit panel). They are drawn in a
/// palette of their own, not in the villagers' skin tones, so a tribe colour reaches them by
/// brightness: each skin colour takes the place its brightness has on the player's skin ramp
/// (between two of its ten levels), and becomes the target colour's ramp at that place, scaled to
/// the same brightness. Hair, eyes, cheeks and outlines are not listed and stay as drawn.
/// </summary>
/// <param name="Colours">The face pictures' skin colours (#rrggbb).</param>
/// <param name="ComputerPortraits">Portraits drawn in the computer's skin (the others are in the player's).</param>
public sealed record FaceSkin(IReadOnlyList<string> Colours, IReadOnlyList<int> ComputerPortraits)
{
    /// <summary>
    /// What each face skin colour becomes (0xRRGGBB to 0xRRGGBB) when the skin ramp
    /// <paramref name="source"/> (the player's) is replaced by <paramref name="target"/>.
    /// </summary>
    public IReadOnlyDictionary<int, int> Map(IReadOnlyList<string> source, IReadOnlyList<string> target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        int[] from = Array.ConvertAll(new List<string>(source).ToArray(), s => RgbColor.Parse(s).Value);
        int[] to = Array.ConvertAll(new List<string>(target).ToArray(), s => RgbColor.Parse(s).Value);
        int levels = Math.Min(from.Length, to.Length);
        var map = new Dictionary<int, int>();
        foreach (string colour in Colours)
        {
            int rgb = RgbColor.Parse(colour).Value;
            map[rgb] = Tint(rgb, from, to, levels);
        }
        return map;
    }

    private static int Tint(int rgb, int[] from, int[] to, int levels)
    {
        double value = Brightness(rgb);
        double level = levels - 1;
        if (value >= Brightness(from[0]))
        {
            level = 0;
        }
        else
        {
            for (int i = 0; i + 1 < levels; i++)
            {
                double upper = Brightness(from[i]), lower = Brightness(from[i + 1]);
                if (value <= upper && value >= lower)
                {
                    level = i + (upper > lower ? (upper - value) / (upper - lower) : 0);
                    break;
                }
            }
        }
        int low = (int)Math.Floor(level), high = Math.Min(levels - 1, low + 1);
        double share = level - low;
        double rampValue = (Brightness(from[low]) * (1 - share)) + (Brightness(from[high]) * share);
        double scale = rampValue > 0 ? value / rampValue : 1;
        int Channel(int shift)
        {
            double c = (((to[low] >> shift) & 0xFF) * (1 - share)) + (((to[high] >> shift) & 0xFF) * share);
            return (int)Math.Clamp(Math.Round(c * scale), 0, 255);
        }
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    /// <summary>The brightest channel, 0..1.</summary>
    private static double Brightness(int rgb) => Math.Max((rgb >> 16) & 0xFF, Math.Max((rgb >> 8) & 0xFF, rgb & 0xFF)) / 255.0;
}
