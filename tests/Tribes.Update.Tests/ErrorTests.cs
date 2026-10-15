using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using Tribes.Update;
using Xunit;

namespace Tribes.Update.Tests;

public sealed class ErrorTests
{
    [Fact]
    public void EveryKind_HasItsOwnChineseMessage()
    {
        UpdateErrorKind[] kinds = Enum.GetValues<UpdateErrorKind>();
        string[] messages = kinds.Select(UpdateErrorMessages.For).ToArray();

        Assert.Equal(kinds.Length, messages.Distinct().Count());
        Assert.All(messages, m => Assert.Contains(m, c => c >= 0x4E00 && c <= 0x9FFF));
        Assert.Contains("網路", UpdateErrorMessages.For(UpdateErrorKind.Offline), StringComparison.Ordinal);
        Assert.Contains("空間", UpdateErrorMessages.For(UpdateErrorKind.DiskFull), StringComparison.Ordinal);
        Assert.Contains("簽章", UpdateErrorMessages.For(UpdateErrorKind.BadSignature), StringComparison.Ordinal);
    }

    [Fact]
    public void Exception_CarriesKindAndMessages()
    {
        var error = new UpdateException(UpdateErrorKind.RateLimited, "detail", null, TimeSpan.FromSeconds(9));

        Assert.Equal(UpdateErrorKind.RateLimited, error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(9), error.RetryAfter);
        Assert.Equal("detail", error.Message);
        Assert.Equal(UpdateErrorMessages.For(UpdateErrorKind.RateLimited), error.UserMessage);
        Assert.False(error.IsTransient);
        Assert.Equal(UpdateErrorKind.Unknown, new UpdateException().Kind);
        Assert.Equal("m", new UpdateException("m").Message);
        Assert.IsType<IOException>(new UpdateException("m", new IOException()).InnerException);
    }

    [Theory]
    [InlineData(unchecked((int)0x80070070), true)]
    [InlineData(unchecked((int)0x80070027), true)]
    [InlineData(28, true)]
    [InlineData(122, true)]
    [InlineData(unchecked((int)0x80070020), false)]
    [InlineData(unchecked((int)0x80131620), false)]
    public void DiskFull_IsRecognisedOnEveryPlatform(int hresult, bool full)
    {
        Assert.Equal(full, UpdateErrors.IsDiskFull(new IOException("x", hresult)));
    }

    [Fact]
    public void Storage_And_Network_Mapping()
    {
        Assert.Equal(UpdateErrorKind.DiskFull, UpdateErrors.FromStorage(new IOException("x", 28), "w").Kind);
        Assert.Equal(UpdateErrorKind.StorageError, UpdateErrors.FromStorage(new UnauthorizedAccessException(), "w").Kind);
        var existing = new UpdateException(UpdateErrorKind.NotFound, "n");
        Assert.Same(existing, UpdateErrors.FromStorage(existing, "w"));
        Assert.Same(existing, UpdateErrors.FromNetwork(existing, "w"));
        Assert.Equal(UpdateErrorKind.Offline, UpdateErrors.FromNetwork(new HttpRequestException(), "w").Kind);
        Assert.True(UpdateErrors.IsNetworkFailure(new IOException()));
        Assert.False(UpdateErrors.IsNetworkFailure(new InvalidOperationException()));
    }
}
