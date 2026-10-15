using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.App;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Session;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Logic;
using Tribes.Core.Systems.Missions;
using Tribes.Session;

namespace Tribes.Client.MatchPlay;

/// <summary>
/// The one place the match screen gets its match from: a <see cref="MatchRequest"/> becomes an
/// <see cref="IMatchRunner"/> of the session layer (single player on this device, a network
/// match handed over by the lobby, or a save). Save thumbnails are kept here too.
/// </summary>
public static class MatchConnection
{
    /// <summary>Opens the match <paramref name="request"/> asks for.</summary>
    /// <exception cref="SaveFileException">The save to load cannot be used.</exception>
    public static IMatchRunner Open(MatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode == MatchMode.Campaign && request.SaveToLoad is null && request.Session is null
            && !Tribes.Client.Ui.Dev.DevOptions.Has("match-exact-mission")
            && CampaignFlow.IsLaterPart(Chapters(), request.Mission))
        {
            // A later part is played on from its chapter's start, never started alone (as in the original).
            request = request with { Mission = CampaignFlow.StartMissionFor(Chapters(), request.Mission) };
        }
        return MatchSessions.Open(request);
    }

    /// <summary>The campaign's chapters in order (the campaign list's own table; progress plays no part here).</summary>
    public static IReadOnlyList<CampaignChapter> Chapters() =>
        CampaignChapters.Build(GameApp.Instance.Content.Rules.Missions.MenuMissions, MissionStart.FirstCustomMission, []);

    /// <summary>Where the thumbnail of save <paramref name="stem"/> ("campaign-1") is kept: next to it, as a PNG.</summary>
    public static string ThumbnailPath(string stem) => $"{SaveCatalog.Folder}/{stem}.png";

    /// <summary>The thumbnail of save <paramref name="stem"/>, or null.</summary>
    public static Texture2D? Thumbnail(string stem)
    {
        string path = ThumbnailPath(stem);
        if (!Godot.FileAccess.FileExists(path))
        {
            return null;
        }
        Image image = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
        return image is null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
    }
}
