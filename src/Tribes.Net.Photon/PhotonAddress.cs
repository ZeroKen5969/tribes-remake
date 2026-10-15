using System;
using System.Globalization;

namespace Tribes.Net.Photon;

/// <summary>
/// Addresses of the Photon transport. A room is <c>region/CODE</c> (for example <c>jp/K7QM2P</c>):
/// the region its host created it in and a six-character room code, which is also what players
/// type to join; a code alone means the region the player is connected to. Inside a room, a peer
/// is <c>actor:N</c> (its Photon actor number); host migration uses those.
/// </summary>
public static class PhotonAddress
{
    /// <summary>The length of a room code.</summary>
    public const int CodeLength = 6;

    /// <summary>The characters of room codes: no 0/O or 1/I, so codes can be read out and typed.</summary>
    public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const string ActorPrefix = "actor:";

    /// <summary>The address of room <paramref name="code"/> in <paramref name="region"/>.</summary>
    public static string Room(string region, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return region + "/" + code;
    }

    /// <summary>
    /// Reads a room address or a typed code: <c>region/CODE</c> or <c>CODE</c>, any letter case,
    /// spaces ignored. <paramref name="region"/> is null when no region was given.
    /// </summary>
    public static bool TryParseRoom(string? text, out string? region, out string code)
    {
        region = null;
        code = string.Empty;
        string compact = (text ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        int slash = compact.IndexOf('/', StringComparison.Ordinal);
        string codePart = compact;
        if (slash >= 0)
        {
            string regionPart = compact[..slash].ToLowerInvariant();
            if (!PhotonRegions.IsRegionCode(regionPart))
            {
                return false;
            }

            region = regionPart;
            codePart = compact[(slash + 1)..];
        }

        codePart = codePart.ToUpperInvariant();
        if (!IsCode(codePart))
        {
            region = null;
            return false;
        }

        code = codePart;
        return true;
    }

    /// <summary>Whether <paramref name="text"/> is a room code (upper case).</summary>
    public static bool IsCode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length != CodeLength)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!CodeAlphabet.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A new random room code.</summary>
    internal static string NewCode(Random random)
    {
        Span<char> code = stackalloc char[CodeLength];
        for (int i = 0; i < code.Length; i++)
        {
            code[i] = CodeAlphabet[random.Next(CodeAlphabet.Length)];
        }

        return new string(code);
    }

    /// <summary>The address of an actor in the current room.</summary>
    internal static string Actor(int actor) => ActorPrefix + actor.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads an actor address.</summary>
    internal static bool TryParseActor(string? text, out int actor)
    {
        actor = 0;
        return text is not null && text.StartsWith(ActorPrefix, StringComparison.Ordinal)
            && int.TryParse(text.AsSpan(ActorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out actor)
            && actor > 0;
    }
}
