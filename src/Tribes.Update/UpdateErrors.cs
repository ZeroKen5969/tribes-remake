using System;
using System.IO;
using System.Net.Http;

namespace Tribes.Update;

/// <summary>What went wrong, in the terms the player sees.</summary>
public enum UpdateErrorKind
{
    /// <summary>Anything not covered below.</summary>
    Unknown,

    /// <summary>No connection, DNS failure, or the server stopped answering.</summary>
    Offline,

    /// <summary>GitHub's request limit was reached.</summary>
    RateLimited,

    /// <summary>The token was refused (revoked, expired, or no access to the repository).</summary>
    AuthFailed,

    /// <summary>The release or file does not exist (also what GitHub answers without access to a private repository).</summary>
    NotFound,

    /// <summary>The server answered with an error.</summary>
    ServerError,

    /// <summary>The manifest is unsigned, signed with an unknown key, or the signature does not match.</summary>
    BadSignature,

    /// <summary>A downloaded file's size or SHA-256 differs from the manifest.</summary>
    IntegrityMismatch,

    /// <summary>The manifest cannot be read or is inconsistent with its release.</summary>
    InvalidManifest,

    /// <summary>Not enough storage space.</summary>
    DiskFull,

    /// <summary>A file could not be written, moved or deleted.</summary>
    StorageError,

    /// <summary>The update could not be prepared or installed.</summary>
    InstallFailed,

    /// <summary>The update cannot be applied on this platform or build.</summary>
    Unsupported,
}

/// <summary>
/// An update failure. <see cref="UserMessage"/> is the zh-TW text for the player; the exception
/// message is technical detail for logs. Neither contains tokens or signed download URLs.
/// </summary>
public sealed class UpdateException : Exception
{
    /// <summary>An <see cref="UpdateErrorKind.Unknown"/> failure.</summary>
    public UpdateException()
    {
    }

    /// <summary>An <see cref="UpdateErrorKind.Unknown"/> failure.</summary>
    public UpdateException(string message)
        : base(message)
    {
    }

    /// <summary>An <see cref="UpdateErrorKind.Unknown"/> failure.</summary>
    public UpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>A failure of the given kind.</summary>
    public UpdateException(UpdateErrorKind kind, string message, Exception? innerException = null, TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        Kind = kind;
        RetryAfter = retryAfter;
    }

    /// <summary>The kind of failure.</summary>
    public UpdateErrorKind Kind { get; }

    /// <summary>How long the server asked to wait, when it said so (rate limits).</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The zh-TW message for the player.</summary>
    public string UserMessage => UpdateErrorMessages.For(Kind);

    /// <summary>Whether trying again soon may succeed (network and server trouble).</summary>
    public bool IsTransient => Kind is UpdateErrorKind.Offline or UpdateErrorKind.ServerError;
}

/// <summary>The zh-TW messages shown to the player for each <see cref="UpdateErrorKind"/>.</summary>
public static class UpdateErrorMessages
{
    /// <summary>The message for a kind.</summary>
    public static string For(UpdateErrorKind kind) => kind switch
    {
        UpdateErrorKind.Offline => "無法連線到更新伺服器，請確認網路連線後再試一次。",
        UpdateErrorKind.RateLimited => "檢查更新的次數太頻繁，請稍後再試。",
        UpdateErrorKind.AuthFailed => "更新服務驗證失敗，這個版本已無法自動更新，請下載最新版遊戲。",
        UpdateErrorKind.NotFound => "找不到更新資料，請稍後再試。",
        UpdateErrorKind.ServerError => "更新伺服器暫時無法使用，請稍後再試。",
        UpdateErrorKind.BadSignature => "更新資料的簽章驗證失敗，為了安全已停止更新。",
        UpdateErrorKind.IntegrityMismatch => "下載的檔案不完整或已損毀，請重新下載。",
        UpdateErrorKind.InvalidManifest => "無法辨識更新資訊，請下載最新版遊戲。",
        UpdateErrorKind.DiskFull => "裝置儲存空間不足，請清出空間後再試。",
        UpdateErrorKind.StorageError => "無法寫入更新檔案，請確認儲存空間與權限後再試。",
        UpdateErrorKind.InstallFailed => "安裝更新失敗，遊戲維持目前的版本。",
        UpdateErrorKind.Unsupported => "這個版本不支援自動更新，請手動下載最新版遊戲。",
        _ => "更新時發生未預期的錯誤，請稍後再試。",
    };
}

/// <summary>Turns system exceptions into <see cref="UpdateException"/>s.</summary>
internal static class UpdateErrors
{
    private const int WindowsDiskFull = 0x70;       // ERROR_DISK_FULL
    private const int WindowsHandleDiskFull = 0x27; // ERROR_HANDLE_DISK_FULL
    private const int UnixNoSpace = 28;             // ENOSPC
    private const int UnixQuota = 122;              // EDQUOT on Linux and Android

    /// <summary>Whether an I/O error means the storage is full.</summary>
    internal static bool IsDiskFull(IOException exception)
    {
        int code = exception.HResult;
        int win32 = code & 0xFFFF;
        bool isWin32 = (code & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000);
        return (isWin32 && (win32 == WindowsDiskFull || win32 == WindowsHandleDiskFull))
            || code == UnixNoSpace
            || code == UnixQuota;
    }

    /// <summary>The update error for a failed file operation.</summary>
    internal static UpdateException FromStorage(Exception exception, string what)
    {
        if (exception is UpdateException update)
        {
            return update;
        }

        if (exception is IOException io && IsDiskFull(io))
        {
            return new UpdateException(UpdateErrorKind.DiskFull, $"Storage is full while {what}.", exception);
        }

        return new UpdateException(UpdateErrorKind.StorageError, $"File operation failed while {what}: {exception.GetType().Name}.", exception);
    }

    /// <summary>The update error for a failed request that never got an answer.</summary>
    internal static UpdateException FromNetwork(Exception exception, string what) =>
        exception is UpdateException update
            ? update
            : new UpdateException(UpdateErrorKind.Offline, $"No answer while {what}: {exception.GetType().Name}.", exception);

    /// <summary>Whether an exception is a failure of the connection (not of the local disk).</summary>
    internal static bool IsNetworkFailure(Exception exception) =>
        exception is HttpRequestException or IOException;
}
