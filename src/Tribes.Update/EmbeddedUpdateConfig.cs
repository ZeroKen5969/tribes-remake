using System;
using System.Net.Http;
using System.Text;

namespace Tribes.Update;

/// <summary>
/// The update source built into this library: the repository name and the read-only token,
/// taken from the secrets file at build time (see the project file). The token is stored masked so
/// it does not show up in a plain-text search of the binary; anyone with the app can still recover it.
/// </summary>
public static class EmbeddedUpdateConfig
{
    /// <summary>The embedded repository, <c>owner/name</c>, or empty when the build had no secrets file.</summary>
    public static string Repository => EmbeddedAccessData.Repository;

    /// <summary>Whether this build can check for updates.</summary>
    public static bool IsConfigured =>
        GitHubReleaseClient.IsRepositoryName(EmbeddedAccessData.Repository) && EmbeddedAccessData.Token().Length > 0;

    /// <summary>A release client for the embedded repository and token.</summary>
    public static GitHubReleaseClient CreateClient(HttpClient http)
    {
        if (!IsConfigured)
        {
            throw new UpdateException(UpdateErrorKind.Unsupported, "This build has no update access embedded.");
        }

        return new GitHubReleaseClient(http, EmbeddedAccessData.Repository, TokenMask.Reveal(EmbeddedAccessData.Token(), EmbeddedAccessData.Mask()));
    }
}

/// <summary>
/// The masking of the embedded token: XOR with a 32-byte mask and the byte position. The build
/// task in the project file applies the same scheme.
/// </summary>
internal static class TokenMask
{
    internal static byte[] Apply(byte[] data, byte[] mask)
    {
        if (mask.Length == 0)
        {
            return [];
        }

        byte[] result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ mask[i % mask.Length] ^ (byte)((i * 0x9D) + 0x3B));
        }

        return result;
    }

    internal static byte[] Conceal(string token, byte[] mask) => Apply(Encoding.UTF8.GetBytes(token), mask);

    internal static string Reveal(byte[] data, byte[] mask) => Encoding.UTF8.GetString(Apply(data, mask));
}
