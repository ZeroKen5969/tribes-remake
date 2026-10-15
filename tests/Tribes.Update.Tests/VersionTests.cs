using System;
using System.Linq;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class VersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, "")]
    [InlineData("v0.0.1-test", 0, 0, 1, "test")]
    [InlineData("10.20.30-beta.2", 10, 20, 30, "beta.2")]
    public void Parse_ReadsParts(string text, int major, int minor, int patch, string label)
    {
        AppVersion version = AppVersion.Parse(text);

        Assert.Equal((major, minor, patch, label), (version.Major, version.Minor, version.Patch, version.Label));
        Assert.Equal(label.Length > 0, version.IsPrerelease);
        Assert.Equal(text.TrimStart('v'), version.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-r5")]
    [InlineData("1.2.3-bad_label")]
    [InlineData("1.2.3-.x")]
    [InlineData("1.2.3-a..b")]
    [InlineData("1.2.3-r0")]
    [InlineData("123456789.0.0")]
    public void TryParse_RejectsMalformed(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Fact]
    public void Parse_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => AppVersion.Parse("nope"));
    }

    [Fact]
    public void Constructor_RejectsInvalidParts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppVersion(-1, 0, 0));
        Assert.Throws<ArgumentException>(() => new AppVersion(1, 0, 0, "r2"));
    }

    [Fact]
    public void Compare_OrdersNumericallyAndPrereleaseFirst()
    {
        string[] sorted = ["0.9.9", "1.0.0-1", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.10", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.2.0", "1.10.0", "2.0.0"];
        AppVersion[] shuffled = sorted.Reverse().Select(AppVersion.Parse).ToArray();

        Assert.Equal(sorted, shuffled.Order().Select(v => v.ToString()));
    }

    [Fact]
    public void Operators_FollowCompare()
    {
        AppVersion a = AppVersion.Parse("1.0.0");
        AppVersion b = AppVersion.Parse("1.1.0");
        AppVersion? none = null;

        Assert.True(a < b && a <= b && b > a && b >= a && a != b);
        Assert.True(a == AppVersion.Parse("v1.0.0"));
        Assert.True(none < a && none == null && a > none);
        Assert.Equal(1, a.CompareTo(null));
        Assert.False(a.Equals((object)"1.0.0"));
        Assert.Equal(a.GetHashCode(), AppVersion.Parse("1.0.0").GetHashCode());
    }

    [Theory]
    [InlineData("v1.2.0", "1.2.0", 0)]
    [InlineData("v1.2.0-r3", "1.2.0", 3)]
    [InlineData("v0.0.1-test", "0.0.1-test", 0)]
    [InlineData("v1.0.0-beta-r12", "1.0.0-beta", 12)]
    public void ReleaseTag_ParsesAndFormats(string tag, string version, int revision)
    {
        ReleaseTag parsed = ReleaseTag.TryParse(tag)!;

        Assert.Equal(version, parsed.Version.ToString());
        Assert.Equal(revision, parsed.ContentRevision);
        Assert.Equal(tag, parsed.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1.2.0")]
    [InlineData("v1.2")]
    [InlineData("v1.2.0-r01")]
    [InlineData("v1.2.0-r0")]
    [InlineData("v1.2.0-r123456789")]
    [InlineData("release-1")]
    public void ReleaseTag_RejectsOtherTags(string? tag)
    {
        Assert.Null(ReleaseTag.TryParse(tag));
    }

    [Fact]
    public void ReleaseTag_OrdersByVersionThenRevision()
    {
        ReleaseTag app = ReleaseTag.TryParse("v1.2.0")!;
        ReleaseTag content = ReleaseTag.TryParse("v1.2.0-r2")!;
        ReleaseTag next = ReleaseTag.TryParse("v1.3.0")!;
        ReleaseTag? none = null;

        Assert.True(app < content && content < next && next > app && content >= app && app <= content);
        Assert.True(app == new ReleaseTag(AppVersion.Parse("1.2.0"), 0) && app != content);
        Assert.True(none < app);
        Assert.Equal(1, app.CompareTo(null));
        Assert.False(app.Equals((object)"v1.2.0"));
        Assert.Equal(app.GetHashCode(), ReleaseTag.TryParse("v1.2.0")!.GetHashCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReleaseTag(AppVersion.Parse("1.0.0"), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReleaseTag(AppVersion.Parse("1.0.0"), ReleaseTag.MaxContentRevision + 1));
        Assert.True(AppVersion.Parse("1.0.0-beta.01") != AppVersion.Parse("1.0.0-beta.1"));
    }
}
