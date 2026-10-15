using Godot;
using Tribes.Client.App;

namespace Tribes.Client.Ui;

/// <summary>The interface screens (scenes in client/scenes/ui) and moving between them.</summary>
public static class UiScreens
{
    /// <summary>First scene: loads the content pack and the content, then shows the main menu.</summary>
    public const string Boot = "res://scenes/ui/boot.tscn";

    /// <summary>Main menu.</summary>
    public const string MainMenu = "res://scenes/ui/main_menu.tscn";

    /// <summary>Settings.</summary>
    public const string Settings = "res://scenes/ui/settings.tscn";

    /// <summary>Campaign: missions, briefing, difficulty.</summary>
    public const string Campaign = "res://scenes/ui/campaign.tscn";

    /// <summary>Custom game: map, difficulty, colour.</summary>
    public const string CustomGame = "res://scenes/ui/custom_game.tscn";

    /// <summary>Multiplayer: LAN room browser and the Internet tab.</summary>
    public const string Multiplayer = "res://scenes/ui/multiplayer.tscn";

    /// <summary>A multiplayer room (lobby).</summary>
    public const string Lobby = "res://scenes/ui/lobby.tscn";

    /// <summary>Endless mode: new world, continue, invite.</summary>
    public const string Endless = "res://scenes/ui/endless.tscn";

    /// <summary>Saved games.</summary>
    public const string LoadGame = "res://scenes/ui/load_game.tscn";

    /// <summary>Check for updates.</summary>
    public const string Update = "res://scenes/ui/update.tscn";

    /// <summary>Credits and licences.</summary>
    public const string Credits = "res://scenes/ui/credits.tscn";

    /// <summary>
    /// The match (world view and HUD), made by the match layer. It reads
    /// <see cref="GameApp.Match"/> to know what to start.
    /// </summary>
    public const string Match = "res://scenes/match/match.tscn";

    /// <summary>Shows <paramref name="scene"/> instead of the current screen.</summary>
    public static void Go(string scene) => GameApp.Instance.Show(scene);

    /// <summary>Whether the match scene exists in this build.</summary>
    public static bool MatchAvailable => ResourceLoader.Exists(Match);
}
