using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Tribes.Net.Photon.Tests;

/// <summary>The settings, the AppId embedded at build time, and keeping it out of every log.</summary>
[Collection("Photon diagnostics")]
public sealed class SettingsTests
{
    private const string Sample = "abcdef12-3456-7890-abcd-ef1234567890";

    /// <summary>The AppId in the secrets file the build read, or null when the build had none.</summary>
    private static string? SecretAppId()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string file = Path.Combine(dir.FullName, "secrets", "photon.json");
            if (File.Exists(file))
            {
                Match match = Regex.Match(File.ReadAllText(file), "\"appIdRealtime\"\\s*:\\s*\"([^\"\\\\]*)\"");
                return match.Success && match.Groups[1].Value.Trim().Length > 0 ? match.Groups[1].Value.Trim() : null;
            }

            if (File.Exists(Path.Combine(dir.FullName, "Tribes.sln")))
            {
                return null;
            }
        }

        return null;
    }

    [Fact]
    public void Settings_NeverPrintTheAppId()
    {
        PhotonSettings settings = PhotonSettings.ForAppId(" " + Sample + " ") with { Region = "jp" };

        Assert.Equal(Sample, settings.AppId);
        Assert.DoesNotContain("abcdef12", settings.ToString(), StringComparison.Ordinal);
        Assert.Contains("jp", settings.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef12", new Link.LinkConnectOptions(Sample, "v", null, false, true).ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PhotonSettings.ForAppId(" "));
    }

    [Fact]
    public void Settings_AreChecked()
    {
        PhotonSettings good = PhotonSettings.ForAppId(Sample);
        Assert.Null(good.FindProblem());
        Assert.Equal("eu", good.WithRegion("EU").Region);
        Assert.Null(good.WithRegion("best").Region);
        Assert.NotNull((good with { Region = "Not a region" }).FindProblem());
        Assert.NotNull((good with { AppVersion = " " }).FindProblem());
        Assert.NotNull((good with { ConnectTimeout = TimeSpan.Zero }).FindProblem());
        Assert.NotNull((good with { MaxMessageBytes = 1000 }).FindProblem());
        Assert.NotNull((good with { FragmentBytes = 100 }).FindProblem());
        Assert.NotNull((good with { FragmentBytes = NetProtocol.MaxMessageBytes + 1 }).FindProblem());
        Assert.NotNull((good with { FragmentBytes = 4096, SendWindowBytes = 8192 }).FindProblem());
        Assert.Null((good with { FragmentBytes = 4096, SendWindowBytes = 8192 + PhotonSettings.FragmentHeaderBytes }).FindProblem());
        Assert.StartsWith("tribes-net-", PhotonSettings.DefaultAppVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void Mask_RoundTrips()
    {
        byte[] mask = [1, 2, 3, 250];
        byte[] hidden = AppIdMask.Conceal(Sample, mask);

        Assert.NotEqual(Encoding.UTF8.GetBytes(Sample), hidden);
        Assert.Equal(Sample, AppIdMask.Reveal(hidden, mask));
        Assert.Empty(AppIdMask.Apply(hidden, []));
    }

    [Fact]
    public void EmbeddedAppId_MatchesTheSecretsFile_AndIsNotStoredInPlainText()
    {
        string? secret = SecretAppId();
        if (secret is null)
        {
            Assert.False(PhotonSettings.IsConfigured);
            Assert.Null(PhotonSettings.FromBuild());
            return;
        }

        Assert.True(PhotonSettings.IsConfigured);
        Assert.True(PhotonSettings.FromBuild()!.AppId == secret, "The embedded AppId differs from the secrets file.");

        // Neither the library nor the file generated for it holds the AppId as text.
        byte[] library = File.ReadAllBytes(typeof(PhotonSettings).Assembly.Location);
        Assert.False(Contains(library, Encoding.UTF8.GetBytes(secret)), "The library contains the AppId as UTF-8 text.");
        Assert.False(Contains(library, Encoding.Unicode.GetBytes(secret)), "The library contains the AppId as UTF-16 text.");
    }

    [Fact]
    public void Log_HidesTheAppId_AndThePrefixPhotonPrints()
    {
        _ = PhotonSettings.ForAppId(Sample);
        var lines = new List<string>();
        Action<string>? before = PhotonDiagnostics.Log;
        PhotonDiagnostics.Log = line =>
        {
            if (line.StartsWith("AppID", StringComparison.Ordinal))
            {
                lock (lines)
                {
                    lines.Add(line);
                }
            }
        };
        try
        {
            PhotonLog.Write($"AppID: \"{Sample[..8]}***\" full {Sample.ToUpperInvariant()}");
        }
        finally
        {
            PhotonDiagnostics.Log = before;
        }

        string line = Assert.Single(lines);
        Assert.DoesNotContain("abcdef12", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("***", line, StringComparison.Ordinal);

        // Short test values are no AppIds and do not blank out ordinary text.
        _ = PhotonSettings.ForAppId("x");
        Assert.Equal("text", PhotonLog.Redact("text"));
        PhotonLog.Write("nobody listens");
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        return haystack.AsSpan().IndexOf(needle) >= 0;
    }
}
