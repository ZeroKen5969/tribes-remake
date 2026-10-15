using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Tribes.Client.Ui.Game;

/// <summary>
/// Which campaign missions the player has won, kept in user://progress.json:
/// <c>{"format": "tribes-progress/1", "completed": [0, 1], "difficulty": 0}</c>. Which chapters of
/// the campaign list are open and won follows from it (<see cref="Logic.CampaignChapters"/>). The
/// match layer calls <see cref="MarkCompleted"/> after a victory.
/// </summary>
public sealed class CampaignProgress
{
    private const string FilePath = "user://progress.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>File format name.</summary>
    [JsonPropertyName("format")]
    public string Format { get; set; } = "tribes-progress/1";

    /// <summary>Mission numbers won at least once.</summary>
    public List<int> Completed { get; set; } = new();

    /// <summary>The difficulty chosen last (0 easy, 1 hard).</summary>
    public int Difficulty { get; set; }

    /// <summary>
    /// The one-off tutorial scenes already seen (the rules' tutorial flags without bit 0), carried
    /// from one campaign mission to the next; the match screen writes it after each mission.
    /// </summary>
    public int TutorialFlags { get; set; }

    /// <summary>Reads the saved progress, or none when there is no file or it cannot be read.</summary>
    public static CampaignProgress Load()
    {
        string text = FileAccess.FileExists(FilePath) ? FileAccess.GetFileAsString(FilePath) : "";
        if (text.Length == 0)
        {
            return new CampaignProgress();
        }
        try
        {
            CampaignProgress progress = JsonSerializer.Deserialize<CampaignProgress>(text, Json) ?? new CampaignProgress();
            // "completed": null reads as no list.
            progress.Completed ??= new();
            return progress;
        }
        catch (JsonException e)
        {
            GD.PushWarning($"{FilePath} ignored: {e.Message}");
            return new CampaignProgress();
        }
    }

    /// <summary>Writes the progress.</summary>
    public void Save()
    {
        using FileAccess file = FileAccess.Open(FilePath, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushError($"cannot write {FilePath}: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Whether <paramref name="mission"/> has been won.</summary>
    public bool IsCompleted(int mission) => Completed.Contains(mission);

    /// <summary>Records a victory in <paramref name="mission"/> and saves.</summary>
    public static void MarkCompleted(int mission)
    {
        CampaignProgress progress = Load();
        if (!progress.Completed.Contains(mission))
        {
            progress.Completed.Add(mission);
            progress.Completed.Sort();
            progress.Save();
        }
    }
}
