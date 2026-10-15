using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tribes.Graphics.Model;

namespace Tribes.Graphics.Serialization;

/// <summary>
/// JSON settings shared by every <c>data/client</c> file: camel-case names, enums as camel-case
/// strings, colours as <c>#rrggbb</c>, null fields left out, text other than control characters
/// written as is (UTF-8).
/// </summary>
public static class CatalogJson
{
    /// <summary>The serializer options.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Serializes <paramref name="value"/>, ending with a newline.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    /// <summary>Deserializes a <typeparamref name="T"/>; throws <see cref="InvalidDataException"/> for an empty document.</summary>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("the JSON document is empty");

    /// <summary>Reads a <typeparamref name="T"/> from a UTF-8 file.</summary>
    public static T Read<T>(string path) => Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>Writes <paramref name="value"/> to a UTF-8 file (without byte order mark), creating its directory.</summary>
    public static void Write<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, Serialize(value), new UTF8Encoding(false));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new RgbColorConverter());
        options.MakeReadOnly();
        return options;
    }

    private sealed class RgbColorConverter : JsonConverter<RgbColor>
    {
        public override RgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            RgbColor.Parse(reader.GetString() ?? throw new JsonException("a colour must be a string"));

        public override void Write(Utf8JsonWriter writer, RgbColor value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(value.ToString());
        }
    }
}
