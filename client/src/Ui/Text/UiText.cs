using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Godot;

namespace Tribes.Client.Ui.Text;

/// <summary>A language the interface can be shown in.</summary>
/// <param name="Code">Settings code ("zh-TW", "en"), also the string file suffix.</param>
/// <param name="NativeName">The language's own name, shown in the language picker.</param>
/// <param name="Locale">Godot locale.</param>
public sealed record UiLanguage(string Code, string NativeName, string Locale);

/// <summary>
/// The interface texts. Every text is a key ("menu.campaign") looked up in Godot's
/// <see cref="TranslationServer"/>, so labels and buttons whose text is a key switch language by
/// themselves when <see cref="Apply"/> changes it. Two sources fill each language:
/// <list type="bullet">
/// <item>content/client/strings/ui.&lt;code&gt;.json: the texts of the new interface;</item>
/// <item>content/client/strings/&lt;code&gt;.json: the original game's strings, as keys "s.N"
/// (table s), "t.N" (table t) and "d.N" (story pages, N = 71 + page).</item>
/// </list>
/// A key missing in English falls back to Traditional Chinese.
/// </summary>
public static class UiText
{
    /// <summary>The default language.</summary>
    public const string DefaultLanguage = "zh-TW";

    private const string StringsFolder = "res://content/client/strings/";

    /// <summary>The languages of the interface.</summary>
    public static readonly IReadOnlyList<UiLanguage> Languages =
    [
        new("zh-TW", "繁體中文", "zh_TW"),
        new("en", "English", "en"),
    ];

    private static readonly Dictionary<string, Translation> Loaded = new(StringComparer.Ordinal);

    /// <summary>The language shown now.</summary>
    public static string Current { get; private set; } = DefaultLanguage;

    /// <summary>Raised after the language changed; screens refresh the texts they format themselves.</summary>
    public static event Action? Changed;

    /// <summary>The language for a settings code, or the default one when it is not supported.</summary>
    public static UiLanguage Find(string? code)
    {
        foreach (UiLanguage language in Languages)
        {
            if (string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }
        return Languages[0];
    }

    /// <summary>Shows the interface in <paramref name="code"/> (loading it on first use).</summary>
    public static void Apply(string? code)
    {
        UiLanguage language = Find(code);
        EnsureLoaded(DefaultLanguage);
        EnsureLoaded(language.Code);
        Current = language.Code;
        TranslationServer.SetLocale(language.Locale);
        Changed?.Invoke();
    }

    /// <summary>The text for <paramref name="key"/> in the current language.</summary>
    public static string T(string key) => TranslationServer.Translate(key).ToString();

    /// <summary>The text for <paramref name="key"/> with {0}, {1}... replaced by <paramref name="args"/>.</summary>
    public static string F(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(key), args);

    /// <summary>Original string <paramref name="id"/> of table s (menus, buildings, names).</summary>
    public static string S(int id) => T("s." + id.ToString(CultureInfo.InvariantCulture));

    /// <summary>Text of story page <paramref name="page"/> (strings table d, id 71 + page).</summary>
    public static string Page(int page) => T("d." + (71 + page).ToString(CultureInfo.InvariantCulture));

    private static void EnsureLoaded(string code)
    {
        if (Loaded.ContainsKey(code))
        {
            return;
        }
        UiLanguage language = Find(code);
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        bool english = code == "en";
        ReadOriginal(messages, StringsFolder + code + ".json", english);
        ReadInterface(messages, StringsFolder + "ui." + code + ".json");
        if (code != DefaultLanguage && Loaded.TryGetValue(DefaultLanguage, out Translation? fallback))
        {
            foreach (string key in fallback.GetMessageList())
            {
                messages.TryAdd(key, fallback.GetMessage(key));
            }
        }
        var translation = new Translation { Locale = language.Locale };
        foreach ((string key, string text) in messages)
        {
            translation.AddMessage(key, text);
        }
        TranslationServer.AddTranslation(translation);
        Loaded[code] = translation;
    }

    /// <summary>Reads ui.&lt;code&gt;.json: <c>{"format": "tribes-ui-strings/1", "strings": {"key": "text", ...}}</c>.</summary>
    private static void ReadInterface(Dictionary<string, string> messages, string path)
    {
        string json = Godot.FileAccess.GetFileAsString(path);
        if (json.Length == 0)
        {
            GD.PushWarning($"interface strings missing: {path}");
            return;
        }
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonProperty entry in document.RootElement.GetProperty("strings").EnumerateObject())
        {
            messages[entry.Name] = entry.Value.GetString() ?? "";
        }
    }

    /// <summary>Reads the original tables s, t and d of a language file (docs/ASSETS.md「字串」).</summary>
    private static void ReadOriginal(Dictionary<string, string> messages, string path, bool english)
    {
        string json = Godot.FileAccess.GetFileAsString(path);
        if (json.Length == 0)
        {
            GD.PushWarning($"original strings missing: {path}");
            return;
        }
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (string table in new[] { "s", "t", "d" })
        {
            if (!document.RootElement.TryGetProperty(table, out JsonElement entries))
            {
                continue;
            }
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                int id = entry.GetProperty("id").GetInt32();
                string text = entry.GetProperty("text").GetString() ?? "";
                // The original English strings are stored in lower case (its font has capitals only).
                if (english)
                {
                    text = table == "s" ? TitleCase(text) : SentenceCase(text);
                }
                messages[table + "." + id.ToString(CultureInfo.InvariantCulture)] = text;
            }
        }
    }

    /// <summary>"a new home" → "A New Home".</summary>
    public static string TitleCase(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool start = true;
        foreach (char c in text)
        {
            builder.Append(start ? char.ToUpperInvariant(c) : c);
            start = c is ' ' or '-' or '\n' or ':';
        }
        return builder.ToString();
    }

    /// <summary>Capitals at the start of each sentence and for the word "i".</summary>
    public static string SentenceCase(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool start = true;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            bool word = (i == 0 || !char.IsLetter(text[i - 1])) && (i + 1 == text.Length || !char.IsLetter(text[i + 1]));
            if (char.IsLetter(c))
            {
                builder.Append(start || (c == 'i' && word) ? char.ToUpperInvariant(c) : c);
                start = false;
            }
            else
            {
                builder.Append(c);
                if (c is '.' or '!' or '?' or '\n')
                {
                    start = true;
                }
            }
        }
        // "i'm", "i'll", "i've" keep their capital I.
        return builder.Replace(" i'", " I'").ToString();
    }
}
