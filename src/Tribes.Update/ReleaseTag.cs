using System;
using System.Globalization;

namespace Tribes.Update;

/// <summary>
/// The tag of a release: <c>v</c> + app version, plus <c>-r</c> + content revision when the
/// revision is above 0 (<c>v1.2.0</c>, <c>v1.2.0-r3</c>, <c>v0.0.1-test</c>). Releases sort by app
/// version, then content revision. The content revision is one counter across all app versions.
/// </summary>
public sealed class ReleaseTag : IComparable<ReleaseTag>, IEquatable<ReleaseTag>
{
    /// <summary>Creates a tag; the revision must not be negative.</summary>
    public ReleaseTag(AppVersion version, int contentRevision)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentOutOfRangeException.ThrowIfNegative(contentRevision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(contentRevision, MaxContentRevision);
        Version = version;
        ContentRevision = contentRevision;
    }

    /// <summary>The app version.</summary>
    public AppVersion Version { get; }

    /// <summary>The content revision.</summary>
    public int ContentRevision { get; }

    /// <summary>Parses a tag; null when it does not follow the format.</summary>
    public static ReleaseTag? TryParse(string? tag)
    {
        if (string.IsNullOrEmpty(tag) || tag[0] != 'v')
        {
            return null;
        }

        int revision = 0;
        string versionText = tag;
        int dash = tag.LastIndexOf('-');
        if (dash > 0 && IsRevisionSuffix(tag[(dash + 1)..]))
        {
            revision = int.Parse(tag.AsSpan(dash + 2), NumberStyles.None, CultureInfo.InvariantCulture);
            versionText = tag[..dash];
        }

        return AppVersion.TryParse(versionText, out AppVersion? version) ? new ReleaseTag(version, revision) : null;
    }

    /// <inheritdoc/>
    public int CompareTo(ReleaseTag? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Version.CompareTo(other.Version);
        return result != 0 ? result : ContentRevision.CompareTo(other.ContentRevision);
    }

    /// <inheritdoc/>
    public bool Equals(ReleaseTag? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ReleaseTag other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Version, ContentRevision);

    /// <summary>The tag text.</summary>
    public override string ToString() =>
        ContentRevision == 0
            ? "v" + Version
            : string.Create(CultureInfo.InvariantCulture, $"v{Version}-r{ContentRevision}");

    /// <summary>Equality.</summary>
    public static bool operator ==(ReleaseTag? left, ReleaseTag? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(ReleaseTag? left, ReleaseTag? right) => !(left == right);

    /// <summary>Ordering.</summary>
    public static bool operator <(ReleaseTag? left, ReleaseTag? right) => Compare(left, right) < 0;

    /// <summary>Ordering.</summary>
    public static bool operator <=(ReleaseTag? left, ReleaseTag? right) => Compare(left, right) <= 0;

    /// <summary>Ordering.</summary>
    public static bool operator >(ReleaseTag? left, ReleaseTag? right) => Compare(left, right) > 0;

    /// <summary>Ordering.</summary>
    public static bool operator >=(ReleaseTag? left, ReleaseTag? right) => Compare(left, right) >= 0;

    /// <summary>The largest content revision a tag can carry (8 digits).</summary>
    public const int MaxContentRevision = 99_999_999;

    /// <summary><c>r</c> followed by 1 to 8 digits, not starting with 0 (revision 0 has no suffix).</summary>
    internal static bool IsRevisionSuffix(string text)
    {
        if (text.Length < 2 || text.Length > 9 || text[0] != 'r' || text[1] == '0')
        {
            return false;
        }

        for (int i = 1; i < text.Length; i++)
        {
            if (text[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static int Compare(ReleaseTag? left, ReleaseTag? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);
}
