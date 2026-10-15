using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Tribes.Update;

/// <summary>A file attached to a GitHub release, as the releases API lists it.</summary>
/// <param name="Id">The asset id, used to download it through the API.</param>
/// <param name="Name">The file name.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="State"><c>uploaded</c>, or <c>starter</c> for an upload that did not finish.</param>
/// <param name="Digest">GitHub's digest (<c>sha256:…</c>) when it reports one.</param>
public sealed record GitHubAsset(long Id, string Name, long Size, string State, string? Digest)
{
    /// <summary>The SHA-256 from <see cref="Digest"/> in lowercase hex, or null.</summary>
    public string? Sha256 =>
        Digest is not null && Digest.StartsWith("sha256:", StringComparison.Ordinal) ? Digest[7..].ToLowerInvariant() : null;

    /// <summary>Whether the upload finished.</summary>
    public bool IsUploaded => State == "uploaded";

    /// <summary>Reads an asset object of the API.</summary>
    public static GitHubAsset FromJson(JsonElement item) => new(
        Json.RequiredLong(item, "id"),
        Json.RequiredString(item, "name"),
        Json.RequiredLong(item, "size"),
        Json.OptionalString(item, "state") ?? "uploaded",
        Json.OptionalString(item, "digest"));
}

/// <summary>A GitHub release, as the releases API returns it.</summary>
/// <param name="Id">The release id.</param>
/// <param name="TagName">The tag.</param>
/// <param name="Name">The title.</param>
/// <param name="Body">The description.</param>
/// <param name="Draft">Whether it is an unpublished draft (only visible with write access).</param>
/// <param name="Prerelease">Whether it is marked as a pre-release.</param>
/// <param name="UploadUrl">The asset upload URL template, for publishing.</param>
/// <param name="Assets">The attached files.</param>
public sealed record GitHubRelease(
    long Id,
    string TagName,
    string Name,
    string Body,
    bool Draft,
    bool Prerelease,
    string? UploadUrl,
    IReadOnlyList<GitHubAsset> Assets)
{
    /// <summary>The asset with a name, or null.</summary>
    public GitHubAsset? FindAsset(string name) => Assets.FirstOrDefault(a => a.Name == name);

    /// <summary>Reads a release object of the API.</summary>
    public static GitHubRelease FromJson(JsonElement item)
    {
        var assets = new List<GitHubAsset>();
        if (item.TryGetProperty("assets", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            assets.AddRange(list.EnumerateArray().Select(GitHubAsset.FromJson));
        }

        return new GitHubRelease(
            Json.RequiredLong(item, "id"),
            Json.RequiredString(item, "tag_name"),
            Json.OptionalString(item, "name") ?? "",
            Json.OptionalString(item, "body") ?? "",
            Json.RequiredBool(item, "draft"),
            Json.RequiredBool(item, "prerelease"),
            Json.OptionalString(item, "upload_url"),
            assets);
    }

    /// <summary>Reads the array the release list endpoint returns.</summary>
    public static IReadOnlyList<GitHubRelease> ListFromJson(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The release list is not an array.");
        }

        return array.EnumerateArray().Select(FromJson).ToArray();
    }
}
