using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.World;
using Tribes.Net;

namespace Tribes.Session;

/// <summary>A new match world and the tribe each room slot plays in it.</summary>
/// <param name="Simulation">The world, at tick 0.</param>
/// <param name="TribeOfSlot">For each slot 0..3 its tribe, or -1 when the world has none for it.</param>
public sealed record MatchWorld(GameSimulation Simulation, IReadOnlyList<int> TribeOfSlot);

/// <summary>How a match ended.</summary>
/// <param name="WinningTribes">The tribes that won (one team); empty when nobody did.</param>
public sealed record MatchOutcome(IReadOnlyList<int> WinningTribes)
{
    /// <summary>Whether <paramref name="tribe"/> is among the winners.</summary>
    public bool Won(int tribe) => tribe >= 0 && ((ICollection<int>)WinningTribes).Contains(tribe);
}

/// <summary>
/// The rules' side of tribes and their controllers: which tribe a slot plays, what the rules
/// must know when a player leaves, returns or gives up, joining tribes, and who lost or won.
/// Every call happens between ticks in the same order on every peer.
/// </summary>
public interface ITribeControl
{
    /// <summary>Builds the world of a network match.</summary>
    /// <param name="content">The game content.</param>
    /// <param name="plan">The host's game setup.</param>
    /// <param name="roster">The participants when the match started.</param>
    MatchWorld Start(GameContent content, MatchPlan plan, IReadOnlyList<PlayerInfo> roster);

    /// <summary>Tells the rules that the player of <paramref name="tribe"/> now stands at <paramref name="state"/>.</summary>
    void SetPlayerState(GameSimulation simulation, int tribe, int slot, PlayerState state);

    /// <summary>A new tribe for a player who joined the running match, or -1 when the rules cannot add one.</summary>
    int AddTribe(GameSimulation simulation, int slot, int team);

    /// <summary>Whether <paramref name="tribe"/> was wiped out.</summary>
    bool IsDefeated(GameSimulation simulation, int tribe);

    /// <summary>The result once the match is decided, else null.</summary>
    MatchOutcome? Outcome(GameSimulation simulation);
}

/// <summary>
/// The original game's two tribes: the first human slot plays the player tribe, the first
/// computer slot stands for the computer tribe, other slots have no tribe. The rules keep no
/// controllers: absent or finished players' orders are dropped by <see cref="LockstepMatch"/>,
/// and the computer only ever plays its own tribe.
/// </summary>
public sealed class OriginalTribeControl : ITribeControl
{
    /// <summary>The shared instance.</summary>
    public static OriginalTribeControl Instance { get; } = new();

    /// <inheritdoc />
    public MatchWorld Start(GameContent content, MatchPlan plan, IReadOnlyList<PlayerInfo> roster)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(roster);
        GameSimulation simulation = plan.LoadWorld(content) ?? NewWorld(content, plan);
        var tribes = new int[RoomLimits.MaxPlayers];
        Array.Fill(tribes, -1);
        bool human = false, computer = false;
        var slotted = new List<PlayerInfo>(roster);
        slotted.RemoveAll(p => !p.HasSlot);
        slotted.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        foreach (PlayerInfo player in slotted)
        {
            if (player.Kind == PlayerKind.Human && !human)
            {
                tribes[player.Slot] = TribeRoles.Player(simulation.World);
                human = true;
            }
            else if (player.Kind == PlayerKind.Computer && !computer)
            {
                tribes[player.Slot] = TribeRoles.Computer(simulation.World);
                computer = true;
            }
        }
        return new MatchWorld(simulation, tribes);
    }

    /// <summary>A new world of the plan's mission for network play: no opening dialogue, hints never stop the world.</summary>
    /// <exception cref="ArgumentException">The mission does not exist.</exception>
    public static GameSimulation NewWorld(GameContent content, MatchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(plan);
        if (!content.Missions.ContainsKey(plan.MapMission))
        {
            throw new ArgumentException($"Mission {plan.MapMission} does not exist.", nameof(plan));
        }
        return GameSimulation.StartMission(content, plan.MapMission, plan.Difficulty,
            new MissionOptions { Seed = plan.Seed, HintsEnabled = false, ShowOpening = false },
            new SimulationOptions { HintsPauseGame = false });
    }

    /// <inheritdoc />
    public void SetPlayerState(GameSimulation simulation, int tribe, int slot, PlayerState state)
    {
    }

    /// <inheritdoc />
    public int AddTribe(GameSimulation simulation, int slot, int team) => -1;

    /// <inheritdoc />
    public bool IsDefeated(GameSimulation simulation, int tribe)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return simulation.Victory switch
        {
            true => TribeRoles.IsComputer(simulation.World, tribe),
            false => TribeRoles.IsPlayer(simulation.World, tribe),
            null => false,
        };
    }

    /// <inheritdoc />
    public MatchOutcome? Outcome(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return simulation.Victory switch
        {
            true => new MatchOutcome([TribeRoles.Player(simulation.World)]),
            false => new MatchOutcome([TribeRoles.Computer(simulation.World)]),
            null => null,
        };
    }
}
