using System;
using System.Globalization;

namespace Tribes.Update;

/// <summary>
/// An app version <c>MAJOR.MINOR.PATCH</c> with an optional label (<c>1.2.0-beta.1</c>). A labelled
/// version is a pre-release and sorts before the same version without a label. Labels are ASCII
/// letters, digits and dots; <c>r</c> followed only by digits is reserved for the content revision in
/// release tags (<see cref="ReleaseTag"/>).
/// </summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private const int MaxPart = 99_999_999;

    /// <summary>Creates a version; throws on negative parts or an invalid label.</summary>
    public AppVersion(int major, int minor, int patch, string label = "")
    {
        if (major < 0 || minor < 0 || patch < 0 || major > MaxPart || minor > MaxPart || patch > MaxPart)
        {
            throw new ArgumentOutOfRangeException(nameof(major), "Version parts must be between 0 and 99999999.");
        }

        ArgumentNullException.ThrowIfNull(label);
        if (label.Length > 0 && !IsValidLabel(label))
        {
            throw new ArgumentException("A version label is ASCII letters, digits and dots, not r<digits>.", nameof(label));
        }

        Major = major;
        Minor = minor;
        Patch = patch;
        Label = label;
    }

    /// <summary>The major part.</summary>
    public int Major { get; }

    /// <summary>The minor part.</summary>
    public int Minor { get; }

    /// <summary>The patch part.</summary>
    public int Patch { get; }

    /// <summary>The pre-release label, empty for a normal release.</summary>
    public string Label { get; }

    /// <summary>Whether the version has a label.</summary>
    public bool IsPrerelease => Label.Length > 0;

    /// <summary>Parses <c>1.2.3</c>, <c>1.2.3-label</c>, with or without a leading <c>v</c>.</summary>
    public static AppVersion Parse(string text) =>
        TryParse(text, out AppVersion? version)
            ? version
            : throw new FormatException($"'{text}' is not a version (MAJOR.MINOR.PATCH[-label]).");

    /// <summary>Parses like <see cref="Parse"/>; false instead of an exception.</summary>
    public static bool TryParse(string? text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AppVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string body = text[0] == 'v' ? text[1..] : text;
        string label = "";
        int dash = body.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            label = body[(dash + 1)..];
            body = body[..dash];
            if (!IsValidLabel(label))
            {
                return false;
            }
        }

        string[] parts = body.Split('.');
        if (parts.Length != 3
            || !TryParsePart(parts[0], out int major)
            || !TryParsePart(parts[1], out int minor)
            || !TryParsePart(parts[2], out int patch))
        {
            return false;
        }

        version = new AppVersion(major, minor, patch, label);
        return true;
    }

    /// <inheritdoc/>
    public int CompareTo(AppVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        if (result != 0 || Label == other.Label)
        {
            return result;
        }

        // A release sorts after its pre-releases.
        if (Label.Length == 0)
        {
            return 1;
        }

        return other.Label.Length == 0 ? -1 : CompareLabels(Label, other.Label);
    }

    /// <inheritdoc/>
    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Label);

    /// <summary>The text form: <c>1.2.3</c> or <c>1.2.3-label</c>.</summary>
    public override string ToString() =>
        Label.Length == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{Label}");

    /// <summary>Equality.</summary>
    public static bool operator ==(AppVersion? left, AppVersion? right) => left is null ? right is null : left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(AppVersion? left, AppVersion? right) => !(left == right);

    /// <summary>Ordering.</summary>
    public static bool operator <(AppVersion? left, AppVersion? right) => Compare(left, right) < 0;

    /// <summary>Ordering.</summary>
    public static bool operator <=(AppVersion? left, AppVersion? right) => Compare(left, right) <= 0;

    /// <summary>Ordering.</summary>
    public static bool operator >(AppVersion? left, AppVersion? right) => Compare(left, right) > 0;

    /// <summary>Ordering.</summary>
    public static bool operator >=(AppVersion? left, AppVersion? right) => Compare(left, right) >= 0;

    private static int Compare(AppVersion? left, AppVersion? right) =>
        left is null ? (right is null ? 0 : -1) : left.CompareTo(right);

    /// <summary>
    /// Semantic-versioning order of labels: dot-separated parts compared in turn, numbers by value
    /// (beta.2 before beta.10) and before words; a label that is a prefix of another comes first.
    /// </summary>
    private static int CompareLabels(string left, string right)
    {
        string[] a = left.Split('.');
        string[] b = right.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool aNumber = IsNumber(a[i]);
            bool bNumber = IsNumber(b[i]);
            int result = (aNumber, bNumber) switch
            {
                (true, true) => CompareNumbers(a[i], b[i]),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i]),
            };
            if (result != 0)
            {
                return result;
            }
        }

        return a.Length.CompareTo(b.Length);

        static bool IsNumber(string part) => part.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0;

        static int CompareNumbers(string x, string y)
        {
            string tx = x.TrimStart('0');
            string ty = y.TrimStart('0');
            int result = tx.Length != ty.Length ? tx.Length.CompareTo(ty.Length) : string.CompareOrdinal(tx, ty);
            return result != 0 ? result : string.CompareOrdinal(x, y);
        }
    }

    private static bool TryParsePart(string text, out int value)
    {
        value = 0;
        if (text.Length == 0 || text.Length > 8 || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        foreach (char c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool IsValidLabel(string label)
    {
        if (label.Length == 0 || label.Length > 32 || label[0] == '.' || label[^1] == '.' || label.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char c in label)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '.')
            {
                return false;
            }
        }

        // r followed only by digits would read as a content revision in a release tag.
        return !(label.Length > 1 && label[0] == 'r' && label.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0);
    }
}
