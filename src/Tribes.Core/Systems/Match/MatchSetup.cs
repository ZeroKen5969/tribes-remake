using System;
using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>A tribe of a match: who plays it and its team.</summary>
/// <param name="Controller">Who plays it (a human slot or the computer at a difficulty).</param>
/// <param name="Team">Its team (team mates are allies), or <see cref="TribeState.NoTeam"/> to be allied with no one.</param>
public sealed record MatchTribe(TribeController Controller, int Team = TribeState.NoTeam);

/// <summary>
/// The choices a versus or custom match starts with. Tribe numbers are the positions in
/// <see cref="Tribes"/>; every tribe starts with a camp (a chief's hut, a store and eleven adults,
/// as the original's player starts a custom map) placed far from the others on open ground.
/// </summary>
public sealed record MatchSetup
{
    /// <summary>The custom map played: 0 is the first custom map of the campaign's mission list (see <see cref="MatchStart.FirstMapMission"/>); at most <see cref="MatchStart.CampCapacity"/> tribes fit.</summary>
    public int Map { get; init; }

    /// <summary>The seed of the rules' random generator, which also places the camps.</summary>
    public long Seed { get; init; } = 1;

    /// <summary>The difficulty of computer tribes whose controller names none (0 easy, 1 hard).</summary>
    public int Difficulty { get; init; }

    /// <summary>The tribes, in tribe order (at least one).</summary>
    public IReadOnlyList<MatchTribe> Tribes { get; init; } = Array.Empty<MatchTribe>();

    /// <summary>Whether the players start with hints switched on.</summary>
    public bool HintsEnabled { get; init; } = true;

    /// <summary>
    /// Whether the match ends once no two tribes still in it are enemies (the last side standing
    /// wins); off for a game that goes on (endless).
    /// </summary>
    public bool EndsWithLastSide { get; init; } = true;

    /// <summary>The lobby's settings: opening grace, start stock, wild animals (the original's when not set).</summary>
    public WorldSettings Settings { get; init; } = WorldSettings.Original;
}
