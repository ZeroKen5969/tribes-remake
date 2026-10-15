using System;
using System.Collections.Generic;

namespace Tribes.Net.Photon;

/// <summary>The Photon Cloud regions players can pick, and the region code format.</summary>
public static class PhotonRegions
{
    /// <summary>
    /// The public Photon Cloud regions (Photon's region codes). An app may have only some of them
    /// enabled in the Photon dashboard; connecting to another one fails with
    /// <see cref="PhotonError.RegionUnavailable"/>.
    /// </summary>
    public static IReadOnlyList<string> Known { get; } =
        ["asia", "jp", "kr", "hk", "in", "au", "eu", "tr", "uae", "za", "us", "usw", "ussc", "cae", "sa"];

    /// <summary>
    /// The region code for <paramref name="text"/>: lower case without spaces; null for "best"
    /// (empty, "best" or "auto"). Text that is not a region code stays as typed, so
    /// <see cref="PhotonSettings.FindProblem"/> reports it.
    /// </summary>
    public static string? Normalize(string? text)
    {
        string trimmed = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (trimmed.Length == 0 || trimmed is "best" or "auto")
        {
            return null;
        }

        return IsRegionCode(trimmed) ? trimmed : text;
    }

    /// <summary>Whether <paramref name="text"/> looks like a region code: 2 to 6 lower-case letters.</summary>
    public static bool IsRegionCode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length is < 2 or > 6)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (c is < 'a' or > 'z')
            {
                return false;
            }
        }

        return true;
    }
}
