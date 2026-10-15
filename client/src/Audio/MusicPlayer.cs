using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Tribes.Client.App;

namespace Tribes.Client.Audio;

/// <summary>
/// The background music (autoload "Music"). Plays the original songs by mood as
/// data/client/music.json lists them: a random song of the mood, then another one when it ends
/// (never the same twice in a row when the mood has more than one). Changing mood fades the
/// current song out and the new one in; the volume follows <see cref="GameSettings.MusicVolume"/>.
/// </summary>
public partial class MusicPlayer : Node
{
    /// <summary>Menus and normal play.</summary>
    public const string Normal = "normal";

    /// <summary>A global order is running or an attack wave is coming.</summary>
    public const string Tension = "tension";

    /// <summary>Fighting on screen.</summary>
    public const string Combat = "combat";

    private const string MusicFile = "res://content/client/music.json";
    private const string AssetRoot = "res://assets/";
    private const float SilentDb = -40f;
    private const double FadeOutSeconds = 0.8;
    private const double FadeInSeconds = 1.2;

    private readonly Dictionary<int, string> songFiles = new();
    private readonly Dictionary<string, int[]> moods = new(StringComparer.Ordinal);
    private readonly Random random = new();
    private AudioStreamPlayer player = null!;
    private Tween? fade;
    private string mood = "";
    private int lastSong = -1;

    /// <summary>The running music player.</summary>
    public static MusicPlayer? Instance { get; private set; }

    /// <summary>The mood playing now, or "" when stopped.</summary>
    public string Mood => mood;

    /// <inheritdoc/>
    public override void _EnterTree() => Instance = this;

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        player = new AudioStreamPlayer { Name = "Song", VolumeDb = SilentDb };
        AddChild(player);
        player.Finished += PlayNextSong;
        ReadCatalog();
    }

    private void ReadCatalog()
    {
        string json = Godot.FileAccess.GetFileAsString(MusicFile);
        if (json.Length == 0)
        {
            GD.PushWarning($"no music list at {MusicFile}");
            return;
        }
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonElement song in document.RootElement.GetProperty("songs").EnumerateArray())
        {
            songFiles[song.GetProperty("id").GetInt32()] = AssetRoot + song.GetProperty("ogg").GetString();
        }
        foreach (JsonElement entry in document.RootElement.GetProperty("moods").EnumerateArray())
        {
            var ids = new List<int>();
            foreach (JsonElement id in entry.GetProperty("songs").EnumerateArray())
            {
                ids.Add(id.GetInt32());
            }
            moods[entry.GetProperty("name").GetString() ?? ""] = ids.ToArray();
        }
    }

    /// <summary>Plays music of <paramref name="name"/>; nothing changes when that mood already plays.</summary>
    public void PlayMood(string name)
    {
        if (name == mood && player.Playing)
        {
            return;
        }
        if (!moods.ContainsKey(name))
        {
            GD.PushWarning($"unknown music mood {name}");
            return;
        }
        mood = name;
        if (player.Playing)
        {
            FadeTo(SilentDb, FadeOutSeconds, PlayNextSong);
        }
        else
        {
            PlayNextSong();
        }
    }

    /// <summary>Fades the music out and stops it.</summary>
    public void Stop()
    {
        mood = "";
        if (player.Playing)
        {
            FadeTo(SilentDb, FadeOutSeconds, player.Stop);
        }
    }

    /// <summary>Follows a changed music volume setting at once.</summary>
    public void ApplyVolume()
    {
        if (player is null)
        {
            return;
        }
        fade?.Kill();
        player.VolumeDb = TargetDb();
        player.StreamPaused = MusicMuted;
    }

    private static bool MusicMuted => GameApp.Instance.Settings.MusicVolume <= 0.001;

    private static float TargetDb() => MusicMuted ? SilentDb : Mathf.LinearToDb((float)GameApp.Instance.Settings.MusicVolume);

    private void PlayNextSong()
    {
        if (!moods.TryGetValue(mood, out int[]? songs) || songs.Length == 0)
        {
            return;
        }
        int song = songs[random.Next(songs.Length)];
        if (songs.Length > 1 && song == lastSong)
        {
            song = songs[(Array.IndexOf(songs, song) + 1) % songs.Length];
        }
        lastSong = song;
        if (!songFiles.TryGetValue(song, out string? path) || !ResourceLoader.Exists(path))
        {
            GD.PushWarning($"song {song} is missing");
            return;
        }
        player.Stream = GD.Load<AudioStream>(path);
        GD.Print($"MUSIC {mood} song {song}");
        player.VolumeDb = SilentDb;
        player.Play();
        player.StreamPaused = MusicMuted;
        FadeTo(TargetDb(), FadeInSeconds, null);
    }

    private void FadeTo(float volumeDb, double seconds, Action? then)
    {
        fade?.Kill();
        fade = CreateTween();
        fade.TweenProperty(player, "volume_db", volumeDb, seconds);
        if (then is not null)
        {
            fade.TweenCallback(Callable.From(then));
        }
    }
}
