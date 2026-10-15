using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Tribes.Client.App;

/// <summary>
/// The folder the rules library reads the game content from. Each build's content gets its own
/// folder, named by a hash of the files, holding exactly those files: content a later version
/// dropped (for example a removed map) can never be loaded again, and several copies of the game
/// running at once on one computer never write into a folder another one is reading. Folders of
/// other versions are removed when they are not in use.
/// </summary>
public static class ExtractedContent
{
    private const string CompleteMarker = ".complete";
    private const string TemporarySuffix = ".tmp-";
    private const int HashDigits = 16;
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(1);
    private const int MoveAttempts = 40;
    private static readonly TimeSpan MoveRetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Makes sure the folder for <paramref name="files"/> (names relative to the content root, with
    /// '/' between folders) exists under <paramref name="root"/> and returns its path.
    /// </summary>
    public static string Prepare(string root, IReadOnlyList<(string Name, byte[] Bytes)> files)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new ArgumentException("no content files", nameof(files));
        }

        Directory.CreateDirectory(root);
        string name = FolderName(files);
        string target = Path.Combine(root, name);
        if (!IsComplete(target))
        {
            Write(root, target, files);
        }

        RemoveOthers(root, name);
        return target;
    }

    /// <summary>The folder name for <paramref name="files"/>: a hash of their names and bytes.</summary>
    public static string FolderName(IReadOnlyList<(string Name, byte[] Bytes)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach ((string fileName, byte[] bytes) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(fileName));
            hash.AppendData([0]);
            hash.AppendData(BitConverter.GetBytes((long)bytes.Length));
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..HashDigits].ToLowerInvariant();
    }

    private static bool IsComplete(string folder) => File.Exists(Path.Combine(folder, CompleteMarker));

    /// <summary>
    /// Writes into a private temporary folder, then renames it, so a half-written folder is never
    /// used. A virus scanner or another copy of the game may hold one of the files for a moment, so
    /// replacing and renaming are tried again a few times before giving up.
    /// </summary>
    private static void Write(string root, string target, IReadOnlyList<(string Name, byte[] Bytes)> files)
    {
        string temporary = Path.Combine(root, Path.GetFileName(target) + TemporarySuffix + Guid.NewGuid().ToString("N"));
        foreach ((string fileName, byte[] bytes) in files)
        {
            string path = Path.Combine(temporary, fileName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        File.WriteAllBytes(Path.Combine(temporary, CompleteMarker), []);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(target) && !IsComplete(target))
                {
                    // A folder without the marker was never finished: replace it.
                    Directory.Delete(target, recursive: true);
                }

                Directory.Move(temporary, target);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (IsComplete(target))
                {
                    // Another copy of the game finished the same folder first; use that one.
                    TryDelete(temporary);
                    return;
                }

                if (attempt >= MoveAttempts)
                {
                    TryDelete(temporary);
                    throw;
                }

                Thread.Sleep(MoveRetryDelay);
            }
        }
    }

    /// <summary>
    /// Removes everything else under <paramref name="root"/>: other versions' folders, files of the
    /// old single-folder layout, and temporary folders abandoned by a start that did not finish.
    /// Anything another running copy still holds open is left for a later start.
    /// </summary>
    private static void RemoveOthers(string root, string keep)
    {
        foreach (string file in Directory.GetFiles(root))
        {
            TryDelete(file);
        }

        foreach (string folder in Directory.GetDirectories(root))
        {
            string name = Path.GetFileName(folder);
            if (name == keep)
            {
                continue;
            }

            bool writingNow = name.Contains(TemporarySuffix, StringComparison.Ordinal)
                && DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) < AbandonedAfter;
            if (!writingNow)
            {
                TryDelete(folder);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // In use by another copy of the game; a later start removes it.
        }
    }
}
