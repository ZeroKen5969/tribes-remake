using System;
using System.Collections.Generic;
using Tribes.Net.Protocol;

namespace Tribes.Net;

/// <summary>
/// The slot table and each slot's last command number as the simulation has them. Only the
/// batches that ran change it, so every peer holds the same ledger at the same tick. The host's
/// room can be a few commands ahead (changes queued for the next batch); when a peer takes over
/// hosting, the ledger is what it rebuilds the room from.
/// </summary>
internal sealed class MatchLedger
{
    private readonly SortedDictionary<int, PlayerInfo> slots = [];
    private readonly long[] sequences = new long[RoomLimits.MaxPlayers];

    public MatchLedger(IEnumerable<PlayerInfo> players, IReadOnlyList<long>? lastSequences = null)
    {
        foreach (PlayerInfo player in players)
        {
            if (player.HasSlot)
            {
                PlayerPresence presence = player.Presence is PlayerPresence.Absent or PlayerPresence.Forfeited
                    ? player.Presence
                    : PlayerPresence.Active;
                slots[player.Slot] = Normalize(player, presence);
            }
        }

        if (lastSequences is not null)
        {
            for (int slot = 0; slot < sequences.Length && slot < lastSequences.Count; slot++)
            {
                sequences[slot] = lastSequences[slot];
            }
        }
    }

    public static MatchLedger From(LedgerState state) => new(state.Roster, state.Sequences);

    public IReadOnlyList<PlayerInfo> Roster => [.. slots.Values];

    public PlayerInfo? InSlot(int slot) => slots.GetValueOrDefault(slot);

    /// <summary>The highest command number of the slot that has run, 0 when none.</summary>
    public long SequenceOf(int slot) => slot is >= 0 and < RoomLimits.MaxPlayers ? sequences[slot] : 0;

    public LedgerState State => new(Roster, [.. sequences]);

    /// <summary>Takes in a batch that just ran.</summary>
    public void Apply(TickBatch batch)
    {
        foreach (PlayerCommand command in batch.Commands)
        {
            switch (command.Kind)
            {
                case CommandKind.Game:
                    sequences[command.Slot] = Math.Max(sequences[command.Slot], command.Sequence);
                    break;
                case CommandKind.PlayerJoined:
                    slots[command.Slot] = Normalize(command.ReadJoinedPlayer(), PlayerPresence.Active);
                    break;
                case CommandKind.PlayerAbsent:
                    SetPresence(command.Slot, PlayerPresence.Absent);
                    break;
                case CommandKind.PlayerReturned:
                    SetPresence(command.Slot, PlayerPresence.Active);
                    break;
                default:
                    SetPresence(command.Slot, PlayerPresence.Forfeited);
                    break;
            }
        }
    }

    private static PlayerInfo Normalize(PlayerInfo player, PlayerPresence presence) =>
        player with { Presence = presence, IsConnected = false, IsReady = false, IsHost = false };

    private void SetPresence(int slot, PlayerPresence presence)
    {
        if (slots.TryGetValue(slot, out PlayerInfo? player))
        {
            slots[slot] = player with { Presence = presence };
        }
    }
}
