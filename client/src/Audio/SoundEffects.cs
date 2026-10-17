using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Tribes.Client.App;
using Tribes.Client.MatchPlay.Logic;

namespace Tribes.Client.Audio;

/// <summary>
/// The sound effects (autoload "Sounds"): plays the sounds data/client/sounds.json lists at
/// <see cref="GameSettings.EffectsVolume"/> (0 is silent), each at most once per its cooldown and at
/// most <see cref="Voices"/> at a time (the one that has played longest makes way). Every button of
/// the menus and panels clicks as it is pressed.
/// </summary>
public partial class SoundEffects : Node
{
    /// <summary>Sounds playing at the same time.</summary>
    public const int Voices = 8;

    /// <summary>A button with this meta set does not click.</summary>
    public const string SilentMeta = "silent";

    private const string SoundsFile = "res://content/client/sounds.json";
    private const string AssetRoot = "res://assets/";
    private const float Muted = 0.001f;

    private readonly Dictionary<string, Sound> sounds = new(StringComparer.Ordinal);
    private readonly List<AudioStreamPlayer> players = new();

    /// <summary>The running sound effects.</summary>
    public static SoundEffects? Instance { get; private set; }

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
        for (int i = 0; i < Voices; i++)
        {
            var player = new AudioStreamPlayer { Name = "Voice" + i };
            AddChild(player);
            players.Add(player);
        }
        ReadCatalog();
        GetTree().NodeAdded += OnNodeAdded;
    }

    /// <summary>Plays the sound called <paramref name="name"/> (nothing for null, an unknown name, or the volume at 0).</summary>
    public void Play(string? name)
    {
        if (name is null || Volume <= Muted || !sounds.TryGetValue(name, out Sound? sound))
        {
            return;
        }
        ulong now = Time.GetTicksMsec();
        if (sound.LastPlayed is { } last && now - last < sound.CooldownMs)
        {
            return;
        }
        if (sound.Stream is null)
        {
            if (!ResourceLoader.Exists(sound.Path))
            {
                GD.PushWarning($"sound {name} is missing at {sound.Path}");
                sounds.Remove(name);
                return;
            }
            sound.Stream = GD.Load<AudioStream>(sound.Path);
        }
        sound.LastPlayed = now;
        AudioStreamPlayer voice = FreeVoice();
        voice.Stream = sound.Stream;
        voice.VolumeDb = Mathf.LinearToDb(Volume);
        voice.Play();
        if (OS.IsDebugBuild())
        {
            GD.Print($"SOUND {name}");
        }
    }

    /// <summary>Follows a changed effects volume at once (sounds playing now included; 0 stops them).</summary>
    public void ApplyVolume()
    {
        foreach (AudioStreamPlayer player in players)
        {
            if (Volume <= Muted)
            {
                player.Stop();
            }
            else
            {
                player.VolumeDb = Mathf.LinearToDb(Volume);
            }
        }
    }

    private static float Volume => (float)GameApp.Instance.Settings.EffectsVolume;

    private AudioStreamPlayer FreeVoice()
    {
        AudioStreamPlayer longest = players[0];
        foreach (AudioStreamPlayer player in players)
        {
            if (!player.Playing)
            {
                return player;
            }
            if (player.GetPlaybackPosition() > longest.GetPlaybackPosition())
            {
                longest = player;
            }
        }
        return longest;
    }

    private void OnNodeAdded(Node node)
    {
        if (node is BaseButton button && !button.HasMeta(SilentMeta))
        {
            button.Pressed += () => Play(SoundCues.Click);
        }
    }

    private void ReadCatalog()
    {
        string json = Godot.FileAccess.GetFileAsString(SoundsFile);
        if (json.Length == 0)
        {
            GD.PushWarning($"no sound list at {SoundsFile}");
            return;
        }
        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonElement entry in document.RootElement.GetProperty("sounds").EnumerateArray())
        {
            string name = entry.GetProperty("name").GetString() ?? "";
            sounds[name] = new Sound(AssetRoot + entry.GetProperty("ogg").GetString(), (ulong)entry.GetProperty("cooldownMs").GetInt32());
        }
    }

    /// <summary>One listed sound: where it is, how often it may play, and when it last did.</summary>
    private sealed class Sound(string path, ulong cooldownMs)
    {
        public string Path { get; } = path;

        public ulong CooldownMs { get; } = cooldownMs;

        public AudioStream? Stream { get; set; }

        public ulong? LastPlayed { get; set; }
    }
}
