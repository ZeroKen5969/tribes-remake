using System;
using System.Collections.Generic;
using Tribes.Net;

namespace Tribes.Client.App;

/// <summary>Which kind of match the menus start.</summary>
public enum MatchMode
{
    /// <summary>A campaign mission (story, tutorial in mission 0).</summary>
    Campaign,

    /// <summary>A custom game on one of the custom maps against the computer.</summary>
    Custom,

    /// <summary>A multiplayer versus match (LAN or Internet).</summary>
    Versus,

    /// <summary>An endless world (alone or with others joining).</summary>
    Endless,
}

/// <summary>A tribe taking part in a match, as the menus set it up.</summary>
/// <param name="Slot">Tribe slot: 0..3 in rooms; a new endless world numbers its computer tribes after the players.</param>
/// <param name="Name">Name shown for the tribe's player.</param>
/// <param name="IsComputer">Whether the computer plays this tribe.</param>
/// <param name="Colour">Tribe colour 0..7 (data/client/tribe-colours.json), or -1 for the mission's own look (campaign).</param>
/// <param name="Team">Team 0..3 (versus); tribes of one team are allies.</param>
/// <param name="AiLevel">Computer strength: the difficulty for computer tribes, 0 otherwise.</param>
/// <param name="IsLocal">Whether this device plays this tribe.</param>
public sealed record MatchPlayer(int Slot, string Name, bool IsComputer, int Colour, int Team, int AiLevel, bool IsLocal);

/// <summary>
/// The network session a multiplayer match runs on. Exactly one of <see cref="Host"/> and
/// <see cref="Client"/> is set. The lobby hands it over already in the match phase; from then on
/// the match screen calls <c>Update()</c> every frame and closes it when the match ends. A hosted
/// session also holds a share of the Android multicast lock: after closing it, call
/// <c>Tribes.Client.Ui.Net.MulticastLease.Release()</c>.
/// </summary>
/// <param name="Host">This device hosts the room.</param>
/// <param name="Client">This device joined someone's room.</param>
/// <param name="Start">The match start the session reported (players, setup), or null when resuming a save.</param>
public sealed record NetSessionRef(HostSession? Host, ClientSession? Client, MatchStart? Start)
{
    /// <summary>The simulation the session drives, once it exists.</summary>
    public ILockstepSimulation? Simulation => Host?.Simulation ?? Client?.Simulation;
}

/// <summary>Settings of a new endless world.</summary>
/// <param name="Seed">World seed.</param>
/// <param name="StartAreaSize">Side of the start area in cells.</param>
/// <param name="ComputerTribes">Computer tribes at the start; there is no upper limit.</param>
/// <param name="NewTribes">
/// How often new computer tribes appear on new land as the world grows:
/// <see cref="EndlessSettings.NoNewTribes"/> (0) to <see cref="EndlessSettings.ManyNewTribes"/> (3).
/// </param>
/// <param name="DevAllianceHalls">
/// For scripted test runs ("--session-dev-halls"): every people's tribe starts with a finished
/// alliance hall and wood and stones to spare (the session's <c>EndlessPlan.DevAllianceHalls</c>).
/// </param>
public sealed record EndlessSettings(int Seed, int StartAreaSize, int ComputerTribes, int NewTribes = EndlessSettings.SteadyNewTribes,
    bool DevAllianceHalls = false)
{
    /// <summary>No new tribes appear.</summary>
    public const int NoNewTribes = 0;

    /// <summary>A new tribe now and then.</summary>
    public const int FewNewTribes = 1;

    /// <summary>New tribes keep appearing at an even pace.</summary>
    public const int SteadyNewTribes = 2;

    /// <summary>Many new tribes.</summary>
    public const int ManyNewTribes = 3;
}

/// <summary>
/// What the menus ask the match screen (<see cref="Tribes.Client.Ui.UiScreens.Match"/>) to start.
/// The menus put it in <see cref="GameApp.Match"/> before showing the match screen.
/// </summary>
public sealed record MatchRequest
{
    /// <summary>The kind of match.</summary>
    public required MatchMode Mode { get; init; }

    /// <summary>Mission number (missions.json) for campaign and custom games; -1 for others.</summary>
    public int Mission { get; init; } = -1;

    /// <summary>Difficulty 0 (easy) or 1 (hard), as missions-rules.json "difficulties".</summary>
    public int Difficulty { get; init; }

    /// <summary>Whether the tutorial explains things (campaign mission 0 and settings).</summary>
    public bool Tutorial { get; init; }

    /// <summary>Whether hints are shown during play.</summary>
    public bool ShowHints { get; init; } = true;

    /// <summary>
    /// The tribes; the local player's tribe has <see cref="MatchPlayer.IsLocal"/>. A new endless
    /// world lists only the people; its computer tribes are counted in <see cref="Endless"/>.
    /// </summary>
    public IReadOnlyList<MatchPlayer> Players { get; init; } = Array.Empty<MatchPlayer>();

    /// <summary>The network session for multiplayer matches; null when playing alone.</summary>
    public NetSessionRef? Session { get; init; }

    /// <summary>A save to continue (absolute path of the save file), or null for a new match.</summary>
    public string? SaveToLoad { get; init; }

    /// <summary>For a hosted match's save: open its room as an internet room instead of on the local network.</summary>
    public bool ResumeOnInternet { get; init; }

    /// <summary>New endless world settings; null for other modes and when continuing a save.</summary>
    public EndlessSettings? Endless { get; init; }

    /// <summary>The lobby's world settings of a new endless world or match (opening grace, start stock, wild animals); null for the defaults.</summary>
    public Tribes.Core.World.WorldSettings? Settings { get; init; }

    /// <summary>The local player's tribe, if any.</summary>
    public MatchPlayer? LocalPlayer
    {
        get
        {
            foreach (MatchPlayer player in Players)
            {
                if (player.IsLocal)
                {
                    return player;
                }
            }
            return null;
        }
    }
}
