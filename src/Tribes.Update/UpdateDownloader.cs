using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Tribes.Update;

/// <summary>Download progress: bytes on disk so far out of the file size.</summary>
/// <param name="BytesReceived">Bytes downloaded, including a resumed part.</param>
/// <param name="TotalBytes">The file size.</param>
public readonly record struct DownloadProgress(long BytesReceived, long TotalBytes)
{
    /// <summary>Progress from 0 to 1.</summary>
    public double Fraction => TotalBytes <= 0 ? 1 : (double)BytesReceived / TotalBytes;
}

/// <summary>Retry and timeout settings of <see cref="UpdateDownloader"/>.</summary>
public sealed class DownloadOptions
{
    /// <summary>Attempts per file before giving up on network trouble.</summary>
    public int MaxAttempts { get; init; } = 4;

    /// <summary>The wait before the first retry; doubled each time.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long the download may receive nothing before the attempt counts as failed.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Extra free space required besides the file itself.</summary>
    public long FreeSpaceMargin { get; init; } = 8 * 1024 * 1024;

    /// <summary>The wait between attempts (replaced in tests).</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>Free bytes on the volume of a directory, or null when unknown (replaced in tests).</summary>
    internal Func<string, long?> FreeSpace { get; init; } = StorageSpace.FreeBytes;
}

/// <summary>
/// Downloads release assets to disk. The file grows as <c>name.part</c> next to the destination;
/// an interrupted or cancelled download resumes from it (HTTP Range) on the next call. After the
/// last byte the size and SHA-256 are checked against the signed manifest, and only then is the
/// part renamed to the destination, so the destination is either absent or complete and verified.
/// </summary>
public sealed class UpdateDownloader
{
    /// <summary>The suffix of a file being downloaded.</summary>
    public const string PartSuffix = ".part";

    private const int BufferSize = 64 * 1024;

    private readonly IAssetSource source;
    private readonly DownloadOptions options;

    /// <summary>Creates a downloader.</summary>
    public UpdateDownloader(IAssetSource source, DownloadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;
        this.options = options ?? new DownloadOptions();
    }

    /// <summary>
    /// Downloads <paramref name="remote"/> to <paramref name="destinationPath"/> and verifies it
    /// against <paramref name="expected"/>. Returns at once when the destination already holds the
    /// verified file. Network failures are retried; cancellation keeps the part for resuming.
    /// </summary>
    public async Task<string> DownloadAsync(
        GitHubAsset remote,
        ManifestAsset expected,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrEmpty(destinationPath);
        if (remote.Name != expected.Name || remote.Size != expected.Size)
        {
            throw new UpdateException(UpdateErrorKind.IntegrityMismatch, $"The release file {remote.Name} does not match the manifest.");
        }

        string destination = Path.GetFullPath(destinationPath);
        string part = destination + PartSuffix;
        PrepareDirectory(destination);
        if (File.Exists(destination) && await MatchesAsync(destination, expected, cancellationToken).ConfigureAwait(false))
        {
            progress?.Report(new DownloadProgress(expected.Size, expected.Size));
            return destination;
        }

        bool restartedAfterMismatch = false;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                EnsureFreeSpace(destination, expected.Size - PartLength(part));
                await DownloadPartAsync(remote.Id, expected.Size, part, progress, cancellationToken).ConfigureAwait(false);
                if (await MatchesAsync(part, expected, cancellationToken).ConfigureAwait(false))
                {
                    Storage(() => File.Move(part, destination, overwrite: true), "saving the download");
                    return destination;
                }

                // A resumed part can be stale (the file was replaced); start over once.
                Storage(() => File.Delete(part), "removing a damaged download");
                if (restartedAfterMismatch)
                {
                    throw new UpdateException(UpdateErrorKind.IntegrityMismatch, $"{expected.Name} does not match its SHA-256.");
                }

                restartedAfterMismatch = true;
                attempt--;
            }
            catch (Exception e) when (IsRetryable(e, cancellationToken) && attempt < options.MaxAttempts)
            {
                TimeSpan wait = options.RetryDelay * Math.Pow(2, attempt - 1);
                await options.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (IsRetryable(e, cancellationToken))
            {
                throw e is UpdateException update ? update : UpdateErrors.FromNetwork(e, "downloading " + expected.Name);
            }
        }
    }

    /// <summary>Whether a file has the manifest's size and SHA-256.</summary>
    public static async Task<bool> MatchesAsync(string path, ManifestAsset expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != expected.Size)
            {
                return false;
            }

            FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            await using (stream.ConfigureAwait(false))
            {
                byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                return Convert.ToHexString(hash).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw UpdateErrors.FromStorage(e, "checking a downloaded file");
        }
    }

    private async Task DownloadPartAsync(long assetId, long size, string part, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        long offset = PartLength(part);
        if (offset > size)
        {
            Storage(() => File.Delete(part), "removing a damaged download");
            offset = 0;
        }

        if (offset == size)
        {
            return;
        }

        using HttpResponseMessage response = await source.OpenAssetAsync(assetId, offset, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            Storage(() => File.Delete(part), "restarting a download");
            throw new TransientDownloadException("The server refused to resume; restarting.");
        }

        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange?.From != offset)
            {
                Storage(() => File.Delete(part), "restarting a download");
                throw new TransientDownloadException("The server resumed at another position; restarting.");
            }
        }
        else
        {
            offset = 0; // The server sent the whole file.
        }

        progress?.Report(new DownloadProgress(offset, size));
        FileStream file = OpenPart(part, offset);
        await using (file.ConfigureAwait(false))
        {
            Stream body;
            try
            {
                body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (UpdateErrors.IsNetworkFailure(e))
            {
                throw new TransientDownloadException("The download did not start.", e);
            }

            await using (body.ConfigureAwait(false))
            {
                await CopyAsync(body, file, offset, size, progress, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task CopyAsync(Stream body, FileStream file, long offset, long size, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[BufferSize];
        long received = offset;
        while (true)
        {
            int read;
            using (var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                stall.CancelAfter(options.StallTimeout);
                try
                {
                    read = await body.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (UpdateErrors.IsNetworkFailure(e) || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    throw new TransientDownloadException("The connection broke during the download.", e);
                }
            }

            if (read == 0)
            {
                break;
            }

            if (received + read > size)
            {
                file.Close();
                Storage(() => File.Delete(file.Name), "removing a damaged download");
                throw new UpdateException(UpdateErrorKind.IntegrityMismatch, "The server sent more bytes than the manifest lists.");
            }

            try
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw UpdateErrors.FromStorage(e, "writing the download");
            }

            received += read;
            progress?.Report(new DownloadProgress(received, size));
        }

        if (received < size)
        {
            throw new TransientDownloadException("The connection closed before the end of the file.");
        }
    }

    private void EnsureFreeSpace(string destination, long needed)
    {
        long? free = options.FreeSpace(Path.GetDirectoryName(destination)!);
        if (free is long bytes && bytes < needed + options.FreeSpaceMargin)
        {
            throw new UpdateException(UpdateErrorKind.DiskFull, "Not enough free space for the download.");
        }
    }

    private static bool IsRetryable(Exception e, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && (e is TransientDownloadException || (e is UpdateException update && update.IsTransient));

    private static FileStream OpenPart(string part, long offset)
    {
        try
        {
            var file = new FileStream(part, offset == 0 ? FileMode.Create : FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            file.SetLength(offset);
            file.Position = offset;
            return file;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw UpdateErrors.FromStorage(e, "opening the download file");
        }
    }

    private static void PrepareDirectory(string destination)
    {
        Storage(() => Directory.CreateDirectory(Path.GetDirectoryName(destination)!), "creating the download folder");
    }

    private static long PartLength(string part)
    {
        var info = new FileInfo(part);
        return info.Exists ? info.Length : 0;
    }

    private static void Storage(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw UpdateErrors.FromStorage(e, what);
        }
    }

    /// <summary>A failed attempt worth retrying; never leaves the downloader.</summary>
    private sealed class TransientDownloadException : Exception
    {
        public TransientDownloadException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }
}

/// <summary>Free space of a volume.</summary>
public static class StorageSpace
{
    /// <summary>
    /// Free bytes on the volume of a directory, or null when it cannot be told (then running out
    /// of space shows up as <see cref="UpdateErrorKind.DiskFull"/> while writing).
    /// </summary>
    public static long? FreeBytes(string directory)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            string? root = Path.GetPathRoot(Path.GetFullPath(directory));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
