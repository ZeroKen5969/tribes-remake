using System;
using Tribes.Core.Simulation;
using Tribes.Net;

namespace Tribes.Session;

/// <summary>
/// A running match as the match screen sees it, single player or network. Call
/// <see cref="Update"/> every frame on the main thread; every event is raised from it (or from
/// the call that caused it). Draw <see cref="Simulation"/>'s world; send orders through
/// <see cref="Orders"/>.
/// </summary>
public interface IMatchRunner : IDisposable
{
    /// <summary>Campaign, custom, versus or endless.</summary>
    MatchKind Match { get; }

    /// <summary>Whether other devices take part.</summary>
    bool IsNetwork { get; }

    /// <summary>Whether this device keeps the match's records (single player, or the network host).</summary>
    bool IsHost { get; }

    /// <summary>Where the runner is.</summary>
    MatchPhase Phase { get; }

    /// <summary>The rules; may be replaced after a resynchronisation (see <see cref="SimulationReplaced"/>).</summary>
    GameSimulation Simulation { get; }

    /// <summary>The participants and where they stand.</summary>
    PlayerTable Players { get; }

    /// <summary>The local participant's public id.</summary>
    Guid LocalPlayerId { get; }

    /// <summary>The local player's tribe while playing, else -1.</summary>
    int LocalTribe { get; }

    /// <summary>Whose view (fog of war) to show: the local tribe, the former tribe of a finished player, or for spectators the first human tribe.</summary>
    int ViewTribe { get; }

    /// <summary>What the local player is.</summary>
    LocalRole Role { get; }

    /// <summary>Where orders go.</summary>
    IOrderSink Orders { get; }

    /// <summary>The result once decided.</summary>
    MatchOutcome? Outcome { get; }

    /// <summary>Why the runner stopped, once it did.</summary>
    MatchStop? StopInfo { get; }

    /// <summary>Whether the match is paused (or held).</summary>
    bool IsPaused { get; }

    /// <summary>Whether the local player may pause.</summary>
    bool CanPause { get; }

    /// <summary>Whether the local player may change the speed.</summary>
    bool CanChangeSpeed { get; }

    /// <summary>Game speed in percent of normal.</summary>
    int SpeedPercent { get; }

    /// <summary>Real seconds per tick at the current speed.</summary>
    double TickSeconds { get; }

    /// <summary>How far the time is from the last tick to the next one, 0 to 1 (for smooth drawing).</summary>
    double TickAlpha { get; }

    /// <summary>Single player: the unit roster is open (a tutorial step waits for it). Ignored in network play.</summary>
    bool RosterOpen { get; set; }

    /// <summary>A tick ran.</summary>
    event EventHandler<MatchTickEventArgs>? TickRan;

    /// <summary>The player table or the connection states changed.</summary>
    event EventHandler? PlayersChanged;

    /// <summary>Something to tell the players.</summary>
    event EventHandler<MatchNoticeEventArgs>? Notice;

    /// <summary>The result was decided.</summary>
    event EventHandler<MatchOverEventArgs>? MatchOver;

    /// <summary>The runner stopped.</summary>
    event EventHandler<MatchStoppedEventArgs>? Stopped;

    /// <summary><see cref="Phase"/> changed.</summary>
    event EventHandler? PhaseChanged;

    /// <summary><see cref="Simulation"/> is a new object (draw the new world).</summary>
    event EventHandler? SimulationReplaced;

    /// <summary>Runs the match for <paramref name="deltaSeconds"/> of real time.</summary>
    void Update(double deltaSeconds);

    /// <summary>
    /// Single player: a menu, hint or help is open. Ticks still run, and orders given meanwhile
    /// are carried out, but the world does not move. Ignored in network play.
    /// </summary>
    void SetHeld(bool held);

    /// <summary>Pauses or continues (network clients ask the host).</summary>
    RequestResult SetPaused(bool paused);

    /// <summary>Changes the speed (network clients ask the host).</summary>
    RequestResult SetSpeed(int speedPercent);

    /// <summary>Gives up (network play; final for the match).</summary>
    RequestResult Forfeit();

    /// <summary>
    /// Saves to <paramref name="slot"/> ("auto" or "1".."5"): the state is taken now, the file is
    /// written on a background thread (one save at a time, in order). A <see cref="MatchNoticeKind.Saved"/>
    /// or <see cref="MatchNoticeKind.SaveFailed"/> notice follows from <see cref="Update"/>. Null
    /// when this device cannot save.
    /// </summary>
    SaveSlot? Save(string slot);

    /// <summary>
    /// Writes <paramref name="picture"/> as the picture of the newest save asked for in
    /// <paramref name="slot"/> (shrunk to 160 wide and encoded in the background, after that save).
    /// It is dropped when a newer save of the slot came in between. False when this runner made no
    /// save of the slot.
    /// </summary>
    bool AttachPicture(string slot, Pixels picture);

    /// <summary>Saves taken but not written yet.</summary>
    int PendingSaves { get; }

    /// <summary>
    /// Waits until every save asked for is written and raises their notices; false when one failed.
    /// Call it before reading a save file this match may still be writing.
    /// </summary>
    bool FlushSaves();

    /// <summary>Leaves the match: the host saves to "auto" first and closes it; clients leave (they can come back).</summary>
    void Quit();
}
