using System;
using System.Text.Json;
using Godot;

namespace Tribes.Client.App;

/// <summary>The player's settings, kept in user://settings.json.</summary>
public sealed class GameSettings
{
    private const string FilePath = "user://settings.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Interface language: "zh-TW" or one of the original game's languages.</summary>
    public string Language { get; set; } = "zh-TW";

    /// <summary>Music volume, 0..1.</summary>
    public double MusicVolume { get; set; } = 0.8;

    /// <summary>Sound effects volume, 0..1.</summary>
    public double EffectsVolume { get; set; } = 0.8;

    /// <summary>Extra interface scale on top of the automatic one (1, 2 or 3).</summary>
    public int InterfaceScale { get; set; } = 1;

    /// <summary>Whether hints are shown during play.</summary>
    public bool ShowHints { get; set; } = true;

    /// <summary>Whether the tutorial explains things in the first missions.</summary>
    public bool Tutorial { get; set; } = true;

    /// <summary>On PC: whether the view scrolls when the mouse touches the window edge.</summary>
    public bool EdgeScroll { get; set; } = true;

    /// <summary>In matches with other people: whether the list of players is shown in its corner.</summary>
    public bool ShowPlayers { get; set; } = true;

    /// <summary>On PC: whether the game fills the screen (borderless, the desktop's resolution) instead of a window.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>Whether a villager's panel shows the details (skills) at the start of each match; the panel's button changes them for that match only.</summary>
    public bool UnitCardDetailed { get; set; }

    /// <summary>Whether the selection panel is folded into a small tab (face and name) until the player opens it.</summary>
    public bool SelectionPanelFolded { get; set; }

    /// <summary>
    /// Whose task icons the map shows (the original's 資訊：全部／已選取／無): 0 all the player's
    /// villagers, 1 the selected ones (and, with a mouse, the one pointed at), 2 none.
    /// </summary>
    public int TaskIcons { get; set; } = 1;

    /// <summary>Name shown to other players.</summary>
    public string PlayerName { get; set; } = "Player";

    /// <summary>Identity kept across sessions so a player can rejoin a match.</summary>
    public string PlayerId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Reads the saved settings, or defaults when there are none or they cannot be read.</summary>
    public static GameSettings Load()
    {
        if (!FileAccess.FileExists(FilePath))
        {
            return new GameSettings();
        }
        try
        {
            using FileAccess file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Read);
            return JsonSerializer.Deserialize<GameSettings>(file.GetAsText(), Json) ?? new GameSettings();
        }
        catch (JsonException e)
        {
            GD.PushWarning($"settings ignored: {e.Message}");
            return new GameSettings();
        }
    }

    /// <summary>Writes the settings.</summary>
    public void Save()
    {
        using FileAccess file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PushError($"cannot write {FilePath}: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(JsonSerializer.Serialize(this, Json));
    }
}
