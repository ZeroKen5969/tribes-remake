using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;
using Tribes.Client.Ui.Text;
using Tribes.Net;

namespace Tribes.Client.Ui.Net;

/// <summary>One of the 8 tribe colours.</summary>
/// <param name="Id">Colour id 0..7, as the network layer and the rules use it.</param>
/// <param name="Swatch">Colour shown in the menus.</param>
/// <param name="Name">Name in the current language.</param>
public sealed record TribeColour(int Id, Color Swatch, string Name);

/// <summary>
/// The tribe colours of data/client/tribe-colours.json (written by the world renderer), read
/// each time the list is asked for so a content update is seen. The file is an array of entries
/// (or an object holding one under "colours"/"colors") with an id, a "#rrggbb" colour and an
/// optional name ("name" or per-language names). When the file is missing the menus show
/// numbered placeholder colours (<see cref="ArePlaceholders"/>).
/// </summary>
public static class TribeColours
{
    private const string FilePath = "res://content/client/tribe-colours.json";
    private static readonly string[] ColourKeys = ["swatch", "display", "menu", "color", "colour", "rgb", "skin", "base"];
    private static readonly string[] FallbackColours = ["#d83020", "#2860d8", "#30a030", "#e8c020", "#9040c0", "#e07818", "#28b0c0", "#e8e8e0"];

    /// <summary>Whether the last <see cref="All"/> came from placeholders.</summary>
    public static bool ArePlaceholders { get; private set; }

    /// <summary>The 8 colours.</summary>
    public static IReadOnlyList<TribeColour> All()
    {
        var result = new List<TribeColour>();
        string json = Godot.FileAccess.FileExists(FilePath) ? Godot.FileAccess.GetFileAsString(FilePath) : "";
        if (json.Length > 0)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                ReadEntries(document.RootElement, result);
            }
            catch (JsonException e)
            {
                GD.PushWarning($"{FilePath}: {e.Message}");
                result.Clear();
            }
        }
        ArePlaceholders = result.Count < RoomLimits.ColourCount;
        if (ArePlaceholders)
        {
            result.Clear();
            for (int i = 0; i < RoomLimits.ColourCount; i++)
            {
                result.Add(new TribeColour(i, new Color(FallbackColours[i]), UiText.F("colour.placeholder", i + 1)));
            }
        }
        result.Sort((a, b) => a.Id.CompareTo(b.Id));
        return result;
    }

    /// <summary>Colour <paramref name="id"/>, or a grey when there is none.</summary>
    public static TribeColour Get(int id)
    {
        foreach (TribeColour colour in All())
        {
            if (colour.Id == id)
            {
                return colour;
            }
        }
        return new TribeColour(id, new Color("#808080"), "—");
    }

    private static void ReadEntries(JsonElement root, List<TribeColour> result)
    {
        JsonElement list = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (string name in new[] { "colours", "colors", "tribeColours", "tribeColors", "tribes" })
            {
                if (root.TryGetProperty(name, out JsonElement found))
                {
                    list = found;
                    break;
                }
            }
        }
        if (list.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        int index = 0;
        foreach (JsonElement entry in list.EnumerateArray())
        {
            int id = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out JsonElement idValue) && idValue.TryGetInt32(out int parsed) ? parsed : index;
            index++;
            Color? swatch = FindColour(entry);
            if (swatch is null || id < 0 || id >= RoomLimits.ColourCount)
            {
                continue;
            }
            result.Add(new TribeColour(id, swatch.Value, FindName(entry, id)));
        }
    }

    private static Color? FindColour(JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.String)
        {
            return ParseHex(entry.GetString());
        }
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (string key in ColourKeys)
        {
            if (entry.TryGetProperty(key, out JsonElement value))
            {
                Color? colour = value.ValueKind == JsonValueKind.String ? ParseHex(value.GetString()) : FindColour(value);
                if (colour is not null)
                {
                    return colour;
                }
            }
        }
        foreach (JsonProperty property in entry.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && ParseHex(property.Value.GetString()) is { } colour)
            {
                return colour;
            }
        }
        return null;
    }

    private static string FindName(JsonElement entry, int id)
    {
        if (entry.ValueKind == JsonValueKind.Object)
        {
            // "name" is Traditional Chinese, "nameEn" English (tribes-tribe-colours/1).
            if (UiText.Current == "en" && entry.TryGetProperty("nameEn", out JsonElement english) && english.GetString() is { Length: > 0 } englishName)
            {
                return englishName;
            }
            foreach (string field in new[] { "names", "name" })
            {
                if (!entry.TryGetProperty(field, out JsonElement name))
                {
                    continue;
                }
                if (name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } single)
                {
                    return single;
                }
                if (name.ValueKind == JsonValueKind.Object)
                {
                    foreach (string language in new[] { UiText.Current, UiText.DefaultLanguage, "en" })
                    {
                        if (name.TryGetProperty(language, out JsonElement local) && local.GetString() is { Length: > 0 } text)
                        {
                            return text;
                        }
                    }
                }
            }
        }
        return UiText.F("colour.placeholder", id + 1);
    }

    private static Color? ParseHex(string? text)
    {
        if (text is null || text.Length != 7 || text[0] != '#')
        {
            return null;
        }
        return int.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _) ? new Color(text) : null;
    }
}
