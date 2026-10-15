using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Net;

namespace Tribes.Session.Tests.Support;

/// <summary>
/// For tests in the original two-tribe world: the first two human slots play tribes 0 and 1, so
/// orders of two peers both reach the rules. Wraps <see cref="OriginalTribeControl"/> otherwise.
/// </summary>
internal sealed class TwoHumansControl : ITribeControl
{
    public MatchWorld Start(GameContent content, MatchPlan plan, IReadOnlyList<PlayerInfo> roster)
    {
        MatchWorld world = OriginalTribeControl.Instance.Start(content, plan, roster);
        var tribes = new int[RoomLimits.MaxPlayers];
        Array.Fill(tribes, -1);
        int next = 0;
        var slotted = new List<PlayerInfo>(roster);
        slotted.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        foreach (PlayerInfo player in slotted)
        {
            if (player.HasSlot && player.Kind == PlayerKind.Human && next < 2)
            {
                tribes[player.Slot] = next++;
            }
        }
        return world with { TribeOfSlot = tribes };
    }

    public void SetPlayerState(GameSimulation simulation, int tribe, int slot, PlayerState state)
    {
    }

    public int AddTribe(GameSimulation simulation, int slot, int team) => -1;

    public bool IsDefeated(GameSimulation simulation, int tribe) => false;

    public MatchOutcome? Outcome(GameSimulation simulation) => null;
}

/// <summary>A control whose verdicts the test sets, recording what the match told it.</summary>
internal sealed class ScriptedControl : ITribeControl
{
    public HashSet<int> Defeated { get; } = [];

    public MatchOutcome? Result { get; set; }

    public int NextTribe { get; set; } = -1;

    public List<(int Tribe, int Slot, PlayerState State)> States { get; } = [];

    public List<int> AddedSlots { get; } = [];

    public MatchWorld Start(GameContent content, MatchPlan plan, IReadOnlyList<PlayerInfo> roster) =>
        new TwoHumansControl().Start(content, plan, roster);

    public void SetPlayerState(GameSimulation simulation, int tribe, int slot, PlayerState state) => States.Add((tribe, slot, state));

    public int AddTribe(GameSimulation simulation, int slot, int team)
    {
        AddedSlots.Add(slot);
        return NextTribe;
    }

    public bool IsDefeated(GameSimulation simulation, int tribe) => Defeated.Contains(tribe);

    public MatchOutcome? Outcome(GameSimulation simulation) => Result;
}
