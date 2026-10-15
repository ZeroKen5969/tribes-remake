using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Godot;
using Tribes.Client.App;
using Tribes.Core.Simulation;
using Tribes.Net;
using Tribes.Session;

namespace Tribes.Client.Ui.Net;

/// <summary>
/// A fingerprint of the rules this build runs, for <see cref="HostOptions.RulesFingerprint"/>:
/// two builds with the same version numbers but other rules or data would drift apart in a
/// lockstep match, so the host turns away players and spectators whose fingerprint differs, and
/// room lists mark those rooms 「版本不同」.
/// </summary>
/// <remarks>
/// It hashes the build ids (MVID) of the libraries that compute the match (Tribes.Core,
/// Tribes.Session, Tribes.Net), which the compiler derives from their content, and the rules data
/// the match loads: the packed content (with any installed content pack, which replaces packed
/// files), except the client's own files, line ends ignored. Files left in user://content by an
/// older build are not part of this build and are not counted. The same sources built the same
/// way give the same fingerprint on every platform. Computed once, the first time it is needed.
/// </remarks>
public static class BuildFingerprint
{
    private const string ClientFolder = "client";
    private const string PackedRoot = "res://content";
    private static readonly Lazy<string> Value = new(Compute, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The fingerprint of this build: "r" and 15 hex digits.</summary>
    public static string Current => Value.Value;

    private static string Compute()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("tribes-rules-fingerprint-1"u8);
        foreach (Type rules in new[] { typeof(GameSimulation), typeof(LockstepMatch), typeof(HostSession) })
        {
            hash.AppendData(rules.Assembly.ManifestModule.ModuleVersionId.ToByteArray());
        }

        var files = new List<string>();
        ListPacked(PackedRoot, "", files);
        files.Sort(StringComparer.Ordinal);
        foreach (string name in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(name));
            hash.AppendData([0]);
            using FileAccess input = FileAccess.Open($"{PackedRoot}/{name}", FileAccess.ModeFlags.Read);
            byte[] bytes = input?.GetBuffer((long)input.GetLength()) ?? [];
            hash.AppendData(bytes.Where(b => b != (byte)'\r').ToArray());
        }

        string fingerprint = "r" + Convert.ToHexString(hash.GetHashAndReset())[..15].ToLowerInvariant();
        GD.Print($"NET rules fingerprint {fingerprint} ({files.Count} rules files)");
        return fingerprint;
    }

    /// <summary>
    /// Adds the packed rules files under <paramref name="folder"/> (names relative to the content
    /// root) to <paramref name="files"/>, named as <see cref="ContentStore"/> copies them out.
    /// </summary>
    private static void ListPacked(string folder, string prefix, List<string> files)
    {
        using DirAccess? directory = DirAccess.Open(folder);
        if (directory is null)
        {
            return;
        }

        foreach (string file in directory.GetFiles())
        {
            // Exported games list imported files with a ".remap" or ".import" suffix.
            if (!file.EndsWith(".import", StringComparison.Ordinal))
            {
                files.Add(prefix + (file.EndsWith(".remap", StringComparison.Ordinal) ? file[..^6] : file));
            }
        }

        foreach (string sub in directory.GetDirectories())
        {
            if (prefix.Length > 0 || sub != ClientFolder)
            {
                ListPacked($"{folder}/{sub}", $"{prefix}{sub}/", files);
            }
        }
    }
}
