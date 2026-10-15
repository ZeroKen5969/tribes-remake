using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class DownloaderTests : IDisposable
{
    private static readonly byte[] Payload = TestData.Bytes(300_000, 7);

    private readonly FakeGitHub github = new();
    private readonly HttpClient http;
    private readonly TempDir temp = new();
    private readonly long assetId;
    private readonly List<TimeSpan> waits = [];

    public DownloaderTests()
    {
        http = github.CreateHttpClient();
        assetId = github.AddAsset(github.AddRelease("v1.0.0"), "content-1.0.0-r1.pck", Payload);
    }

    private GitHubAsset Remote => new(assetId, "content-1.0.0-r1.pck", Payload.Length, "uploaded", null);

    private ManifestAsset Expected => TestData.Asset("content-1.0.0-r1.pck", Payload);

    private string Destination => temp.File("content-1.0.0-r1.pck");

    public void Dispose()
    {
        http.Dispose();
        github.Dispose();
        temp.Dispose();
    }

    [Fact]
    public async Task Download_WritesVerifiedFileAndReportsProgress()
    {
        var reports = new List<DownloadProgress>();

        string path = await Downloader().DownloadAsync(Remote, Expected, Destination, new SyncProgress(reports.Add));

        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + UpdateDownloader.PartSuffix));
        Assert.Equal(0, reports[0].BytesReceived);
        Assert.Equal(Payload.Length, reports[^1].BytesReceived);
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.True(reports.Zip(reports.Skip(1)).All(p => p.First.BytesReceived <= p.Second.BytesReceived));
    }

    [Fact]
    public async Task Download_ResumesFromThePartFile()
    {
        await File.WriteAllBytesAsync(Destination + UpdateDownloader.PartSuffix, Payload[..100_000]);

        await Downloader().DownloadAsync(Remote, Expected, Destination);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
        Assert.Contains(github.Requests, r => r.Uri.Host == FakeGitHub.StorageHost && r.RangeFrom == 100_000);
        Assert.All(github.Requests.Where(r => r.Uri.Host == FakeGitHub.StorageHost), r => Assert.Null(r.Authorization));
    }

    [Fact]
    public async Task Download_RestartsWhenTheServerIgnoresRange()
    {
        github.SupportsRange = false;
        await File.WriteAllBytesAsync(Destination + UpdateDownloader.PartSuffix, Payload[..100_000]);

        await Downloader().DownloadAsync(Remote, Expected, Destination);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task Download_RetriesAfterABrokenConnectionAndResumes()
    {
        int storageCalls = 0;
        github.Intercept = request =>
        {
            if (request.RequestUri!.Host != FakeGitHub.StorageHost || Interlocked.Increment(ref storageCalls) > 1)
            {
                return null;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BreakingStream(Payload, 120_000)) };
        };

        await Downloader().DownloadAsync(Remote, Expected, Destination);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
        Assert.Single(waits);
        Assert.Contains(github.Requests, r => r.RangeFrom == 120_000);
    }

    [Fact]
    public async Task Download_RetriesServerErrorsThenGivesUp()
    {
        github.Intercept = _ => new HttpResponseMessage(HttpStatusCode.BadGateway);

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Downloader(maxAttempts: 3).DownloadAsync(Remote, Expected, Destination));

        Assert.Equal(UpdateErrorKind.ServerError, error.Kind);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], waits);
    }

    [Fact]
    public async Task Download_ShortBodyOnLastAttempt_IsOffline()
    {
        github.Intercept = request => request.RequestUri!.Host == FakeGitHub.StorageHost
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload[..10]) }
            : null;

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Downloader(maxAttempts: 1).DownloadAsync(Remote, Expected, Destination));

        Assert.Equal(UpdateErrorKind.Offline, error.Kind);
    }

    [Fact]
    public async Task Download_AuthFailureIsNotRetried()
    {
        github.Intercept = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Downloader().DownloadAsync(Remote, Expected, Destination));

        Assert.Equal(UpdateErrorKind.AuthFailed, error.Kind);
        Assert.Empty(waits);
    }

    [Fact]
    public async Task Download_CancelKeepsThePartForResuming()
    {
        using var cancel = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            if (p.BytesReceived > 64 * 1024)
            {
                cancel.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader().DownloadAsync(Remote, Expected, Destination, progress, cancel.Token));

        long kept = new FileInfo(Destination + UpdateDownloader.PartSuffix).Length;
        Assert.InRange(kept, 1, Payload.Length - 1);
        Assert.False(File.Exists(Destination));

        await Downloader().DownloadAsync(Remote, Expected, Destination);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
        Assert.Contains(github.Requests, r => r.RangeFrom == kept);
    }

    [Fact]
    public async Task Download_WrongBytes_IsAnIntegrityErrorAndLeavesNothing()
    {
        byte[] other = (byte[])Payload.Clone();
        other[12345] ^= 0xFF;
        github.Served[assetId] = other;

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Downloader().DownloadAsync(Remote, Expected, Destination));

        Assert.Equal(UpdateErrorKind.IntegrityMismatch, error.Kind);
        Assert.Empty(Directory.GetFiles(temp.Path));
        Assert.Equal(2, github.Requests.Count(r => r.Uri.Host == FakeGitHub.StorageHost));
    }

    [Fact]
    public async Task Download_StalePartIsReplacedOnce()
    {
        byte[] stale = TestData.Bytes(100_000, 99);
        await File.WriteAllBytesAsync(Destination + UpdateDownloader.PartSuffix, stale);

        await Downloader().DownloadAsync(Remote, Expected, Destination);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task Download_TooManyBytes_IsAnIntegrityError()
    {
        github.Served[assetId] = [.. Payload, 1, 2, 3];
        github.SupportsRange = false;

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Downloader().DownloadAsync(Remote, Expected, Destination));

        Assert.Equal(UpdateErrorKind.IntegrityMismatch, error.Kind);
        Assert.False(File.Exists(Destination + UpdateDownloader.PartSuffix));
    }

    [Fact]
    public async Task Download_OversizedPartAndRangeErrors_Restart()
    {
        await File.WriteAllBytesAsync(Destination + UpdateDownloader.PartSuffix, new byte[Payload.Length + 5]);

        await Downloader().DownloadAsync(Remote, Expected, Destination);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task Download_RangeRefused_RestartsFromZero()
    {
        await File.WriteAllBytesAsync(Destination + UpdateDownloader.PartSuffix, Payload[..1000]);
        bool refused = false;
        github.Intercept = request =>
        {
            if (request.RequestUri!.Host != FakeGitHub.StorageHost || refused)
            {
                return null;
            }

            refused = true;
            return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
        };

        await Downloader().DownloadAsync(Remote, Expected, Destination);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
        Assert.Single(waits);
    }

    [Fact]
    public async Task Download_WrongResumePosition_Restarts()
    {
        await File.WriteAllBytesAsync(Destination + UpdateDownloader.PartSuffix, Payload[..1000]);
        bool wrong = false;
        github.Intercept = request =>
        {
            if (request.RequestUri!.Host != FakeGitHub.StorageHost || wrong)
            {
                return null;
            }

            wrong = true;
            var content = new ByteArrayContent(Payload, 500, Payload.Length - 500);
            content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(500, Payload.Length - 1, Payload.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
        };

        await Downloader().DownloadAsync(Remote, Expected, Destination);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
    }

    [Fact]
    public async Task Download_StalledConnection_IsRetried()
    {
        int storageCalls = 0;
        github.Intercept = request =>
            request.RequestUri!.Host == FakeGitHub.StorageHost && Interlocked.Increment(ref storageCalls) == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) }
                : null;

        await Downloader(stall: TimeSpan.FromMilliseconds(200)).DownloadAsync(Remote, Expected, Destination);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(Destination));
        Assert.Single(waits);
    }

    [Fact]
    public async Task Download_NotEnoughSpace_IsDiskFull()
    {
        var downloader = new UpdateDownloader(github.CreateClient(http), new DownloadOptions { FreeSpace = _ => 1000, Delay = Record });

        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Remote, Expected, Destination));

        Assert.Equal(UpdateErrorKind.DiskFull, error.Kind);
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task Download_AlreadyThere_SkipsTheNetwork()
    {
        await File.WriteAllBytesAsync(Destination, Payload);

        await Downloader().DownloadAsync(Remote, Expected, Destination);

        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task Download_ReleaseFileNotMatchingTheManifest_IsRejected()
    {
        UpdateException error = await Assert.ThrowsAsync<UpdateException>(() => Downloader().DownloadAsync(Remote with { Size = 5 }, Expected, Destination));

        Assert.Equal(UpdateErrorKind.IntegrityMismatch, error.Kind);
    }

    [Fact]
    public async Task Matches_ChecksSizeAndHash()
    {
        await File.WriteAllBytesAsync(Destination, Payload);

        Assert.True(await UpdateDownloader.MatchesAsync(Destination, Expected));
        Assert.False(await UpdateDownloader.MatchesAsync(Destination, Expected with { Sha256 = new string('0', 64) }));
        Assert.False(await UpdateDownloader.MatchesAsync(temp.File("missing"), Expected));
    }

    [Fact]
    public void Progress_Fraction()
    {
        Assert.Equal(0.5, new DownloadProgress(5, 10).Fraction);
        Assert.Equal(1, new DownloadProgress(0, 0).Fraction);
    }

    [Fact]
    public void FreeSpace_IsKnownOnWindows()
    {
        long? free = StorageSpace.FreeBytes(temp.Path);

        Assert.Equal(OperatingSystem.IsWindows(), free is > 0);
    }

    private UpdateDownloader Downloader(int maxAttempts = 4, TimeSpan? stall = null) =>
        new(github.CreateClient(http), new DownloadOptions
        {
            MaxAttempts = maxAttempts,
            Delay = Record,
            FreeSpace = _ => null,
            StallTimeout = stall ?? TimeSpan.FromSeconds(30),
        });

    private Task Record(TimeSpan wait, CancellationToken cancellationToken)
    {
        waits.Add(wait);
        return Task.CompletedTask;
    }

    /// <summary>Reports synchronously (Progress&lt;T&gt; would post to a thread pool).</summary>
    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    /// <summary>Serves bytes, then fails like a dropped connection.</summary>
    private sealed class BreakingStream(byte[] data, int breakAt) : MemoryStream(data[..breakAt])
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            return read > 0 ? read : throw new IOException("connection reset");
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer, cancellationToken);
            return read > 0 ? read : throw new IOException("connection reset");
        }
    }

    /// <summary>Never delivers a byte.</summary>
    private sealed class StallingStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
