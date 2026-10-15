using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tribes.Update;

/// <summary>
/// Reads and writes <c>manifest.json</c>. Written by hand with the JSON reader and writer (no
/// reflection), so it keeps working when the app is trimmed.
/// </summary>
public static class ManifestJson
{
    /// <summary>The largest manifest the client accepts.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>Writer settings: indented, nothing escaped that JSON allows as is, so the notes stay readable.</summary>
    internal static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The UTF-8 JSON of a manifest. The same manifest always gives the same bytes.</summary>
    public static byte[] Serialize(UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", UpdateManifest.CurrentSchemaVersion);
            writer.WriteString("appVersion", manifest.AppVersion.ToString());
            writer.WriteNumber("contentRevision", manifest.ContentRevision);
            writer.WriteString("minAppVersion", manifest.MinAppVersion.ToString());
            writer.WriteString("notes", manifest.Notes);
            writer.WriteStartArray("assets");
            foreach (ManifestAsset asset in manifest.Assets)
            {
                writer.WriteStartObject();
                writer.WriteString("name", asset.Name);
                writer.WriteString("platform", PlatformName(asset.Platform));
                writer.WriteString("kind", KindName(asset.Kind));
                writer.WriteNumber("size", asset.Size);
                writer.WriteString("sha256", asset.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Reads a manifest; throws <see cref="UpdateException"/> (<see cref="UpdateErrorKind.InvalidManifest"/>)
    /// when it is malformed, too large, or of a newer schema. Unknown properties are ignored.
    /// </summary>
    public static UpdateManifest Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length > MaxBytes)
        {
            throw Invalid("the manifest is larger than allowed");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            int schema = Json.RequiredInt(root, "schemaVersion");
            if (schema != UpdateManifest.CurrentSchemaVersion)
            {
                throw Invalid($"schema version {schema} is not supported");
            }

            var assets = new List<ManifestAsset>();
            foreach (JsonElement item in Json.RequiredArray(root, "assets"))
            {
                assets.Add(new ManifestAsset(
                    Json.RequiredString(item, "name"),
                    ParsePlatform(Json.RequiredString(item, "platform")),
                    ParseKind(Json.RequiredString(item, "kind")),
                    Json.RequiredLong(item, "size"),
                    Json.RequiredString(item, "sha256")));
            }

            return new UpdateManifest(
                ParseVersion(Json.RequiredString(root, "appVersion")),
                Json.RequiredInt(root, "contentRevision"),
                ParseVersion(Json.RequiredString(root, "minAppVersion")),
                Json.RequiredString(root, "notes"),
                assets);
        }
        catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            throw new UpdateException(UpdateErrorKind.InvalidManifest, "The manifest is malformed: " + e.Message, e);
        }
    }

    /// <summary>The manifest name of a platform.</summary>
    public static string PlatformName(UpdatePlatform platform) => platform switch
    {
        UpdatePlatform.Android => "android",
        UpdatePlatform.Windows => "windows",
        _ => "any",
    };

    /// <summary>Parses a manifest platform name.</summary>
    public static UpdatePlatform ParsePlatform(string name) => name switch
    {
        "any" => UpdatePlatform.Any,
        "android" => UpdatePlatform.Android,
        "windows" => UpdatePlatform.Windows,
        _ => throw new FormatException($"Unknown platform '{name}'."),
    };

    private static string KindName(AssetKind kind) => kind == AssetKind.App ? "app" : "content";

    private static AssetKind ParseKind(string name) => name switch
    {
        "app" => AssetKind.App,
        "content" => AssetKind.Content,
        _ => throw new FormatException($"Unknown asset kind '{name}'."),
    };

    private static AppVersion ParseVersion(string text) =>
        AppVersion.TryParse(text, out AppVersion? version) ? version : throw new FormatException($"'{text}' is not a version.");

    private static UpdateException Invalid(string reason) =>
        new(UpdateErrorKind.InvalidManifest, "The manifest is not usable: " + reason + ".");
}

/// <summary>Small strict readers for JSON documents.</summary>
internal static class Json
{
    internal static JsonElement Required(JsonElement obj, string name, JsonValueKind kind)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out JsonElement value) || value.ValueKind != kind)
        {
            throw new FormatException($"Property '{name}' is missing or not {kind}.");
        }

        return value;
    }

    internal static string RequiredString(JsonElement obj, string name) => Required(obj, name, JsonValueKind.String).GetString()!;

    internal static int RequiredInt(JsonElement obj, string name) =>
        Required(obj, name, JsonValueKind.Number).TryGetInt32(out int value) ? value : throw new FormatException($"Property '{name}' is not an int.");

    internal static long RequiredLong(JsonElement obj, string name) =>
        Required(obj, name, JsonValueKind.Number).TryGetInt64(out long value) ? value : throw new FormatException($"Property '{name}' is not an integer.");

    internal static JsonElement.ArrayEnumerator RequiredArray(JsonElement obj, string name) =>
        Required(obj, name, JsonValueKind.Array).EnumerateArray();

    internal static bool RequiredBool(JsonElement obj, string name)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        throw new FormatException($"Property '{name}' is missing or not a boolean.");
    }

    internal static string? OptionalString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
