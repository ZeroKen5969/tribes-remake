using Tribes.Client.App;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Core.Commands;
using Tribes.Core.World;
using Tribes.Session;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>Why the game is held (single player): any reason holds it.</summary>
public enum HoldReason
{
    /// <summary>The player pressed pause.</summary>
    Paused,

    /// <summary>The game menu is open.</summary>
    Menu,

    /// <summary>A hint is shown.</summary>
    Hint,

    /// <summary>A panel of the original's game menu is open (build list, army calls, help, objective); the roster leaves the game running, as in the original.</summary>
    Panel,

    /// <summary>The result screen is shown.</summary>
    Results,

    /// <summary>A question (a confirm dialog) is open.</summary>
    Question,

    /// <summary>A story scene's first step is still sliding in (<see cref="SceneSlide"/>).</summary>
    SceneSlide,
}

/// <summary>What the match interface can ask of the match screen.</summary>
public interface IMatchActions
{
    /// <summary>The match.</summary>
    IMatchRunner Runner { get; }

    /// <summary>What kind of match this is (campaign, custom map, versus, endless).</summary>
    MatchMode Mode { get; }

    /// <summary>The world shown.</summary>
    GameWorld World { get; }

    /// <summary>The tribe this device plays (-1 when watching).</summary>
    int Tribe { get; }

    /// <summary>The tribe whose view is shown.</summary>
    int ViewTribe { get; }

    /// <summary>What the view shows: the fog through the player's (or every human player's) eyes, whose markers and traps are known.</summary>
    Tribes.Client.World.Scene.ViewerSight Sight { get; }

    /// <summary>Whether this device only watches (no orders).</summary>
    bool Spectating { get; }

    /// <summary>The selection.</summary>
    UnitSelection Selection { get; }

    /// <summary>Interface sizes.</summary>
    HudMetrics Metrics { get; }

    /// <summary>The layer dialogs (questions with buttons) are shown on.</summary>
    Godot.Control DialogLayer { get; }

    /// <summary>The colour index of <paramref name="tribe"/>'s tribe.</summary>
    int ColourOf(int tribe);

    /// <summary>Sends an order of this device's tribe (shows a note when it is refused).</summary>
    void Send(GameOrder order);

    /// <summary>Starts placing building kind <paramref name="kind"/>.</summary>
    void BeginPlacement(int kind);

    /// <summary>
    /// Starts placing building kind <paramref name="kind"/> dragged out of the build menu by finger
    /// <paramref name="finger"/> (-1 when it was lifted already), now at screen point
    /// <paramref name="screen"/>: the ghost comes out just above the finger and follows it until
    /// the finger is lifted.
    /// </summary>
    void DragPlacement(int kind, int finger, Godot.Vector2 screen);

    /// <summary>Starts placing a trap of <paramref name="trapKind"/> ordered from <paramref name="armoury"/>.</summary>
    void BeginTrap(int armoury, int trapKind);

    /// <summary>Starts picking the point of an army call.</summary>
    void BeginArmyPoint(ArmyCall call);

    /// <summary>Starts picking the target of a spell.</summary>
    void BeginSpell(int sanctuary, int spell);

    /// <summary>Stops placing or picking.</summary>
    void CancelMode();

    /// <summary>Opens the store menu of <paramref name="store"/> for the selected units (they walk to <paramref name="cell"/>).</summary>
    void OpenStore(int store, Tribes.Core.Content.GridPoint cell);

    /// <summary>Moves the view to map cell (<paramref name="x"/>, <paramref name="y"/>).</summary>
    void CentreOnCell(int x, int y);

    /// <summary>Moves the view to what is selected (or the tribe's first building).</summary>
    void CentreOnSelection();

    /// <summary>Selects <paramref name="unit"/>; <paramref name="centre"/> moves the view to it.</summary>
    void SelectUnit(int unit, bool centre);

    /// <summary>Selects <paramref name="building"/>.</summary>
    void SelectBuilding(int building);

    /// <summary>Holds or lets go of the game for <paramref name="reason"/> (single player only).</summary>
    void Hold(HoldReason reason, bool on);

    /// <summary>Shows a short note.</summary>
    void Note(string text);

    /// <summary>Opens <paramref name="panel"/> in place of the popup that is open.</summary>
    void Open(HudPopup panel);

    /// <summary>The frame from which computer tribes may attack this player's tribe in an endless world, or null.</summary>
    int? GraceEnd();

    /// <summary>Leaves the match for the menus (after saving where that applies).</summary>
    void QuitToMenu();
}
