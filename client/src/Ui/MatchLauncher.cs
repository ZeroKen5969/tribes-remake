using System.Linq;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Widgets;

namespace Tribes.Client.Ui;

/// <summary>Opens the match screen for the request in <see cref="GameApp.Match"/>.</summary>
public static class MatchLauncher
{
    /// <summary>
    /// Shows the match screen; when this build has none yet, says so in a dialog on
    /// <paramref name="overlay"/> and keeps the request.
    /// </summary>
    public static void Launch(Control overlay)
    {
        MatchRequest? request = GameApp.Instance.Match;
        if (request is null)
        {
            return;
        }
        string players = string.Join(", ", request.Players.Select(p => $"{p.Slot}:{p.Name}:{(p.IsComputer ? "cpu" : "human")}:colour{p.Colour}:team{p.Team}"));
        GD.Print($"MATCH_REQUEST mode={request.Mode} mission={request.Mission} difficulty={request.Difficulty} save={request.SaveToLoad ?? "-"} net={(request.Session is null ? "none" : request.Session.Host is not null ? "host" : "client")} endless={request.Endless?.ToString() ?? "-"} players=[{players}]");
        if (UiScreens.MatchAvailable)
        {
            UiScreens.Go(UiScreens.Match);
        }
        else
        {
            ModalDialog.Show(overlay, "match.unavailable", (UiKeys.Ok, null));
        }
    }
}
