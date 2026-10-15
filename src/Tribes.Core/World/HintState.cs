using System;

namespace Tribes.Core.World;

/// <summary>
/// The player's hints: whether they are switched on (a player option the rules read) and the
/// cool-down of each hint channel, so the same kind of hint is not shown again too soon.
/// </summary>
public sealed class HintState
{
    /// <summary>Hint channels.</summary>
    public const int Channels = 6;

    /// <summary>The cool-down value of a channel that may show a hint again.</summary>
    public const int Free = -1;

    private readonly int[] cooldowns = new int[Channels];

    /// <summary>Creates the state of a new game: hints on, every channel free.</summary>
    public HintState()
    {
        Array.Fill(cooldowns, Free);
    }

    /// <summary>Whether the player has hints switched on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Frames since channel <paramref name="channel"/> last showed a hint, or <see cref="Free"/>.</summary>
    public int Cooldown(int channel) => cooldowns[channel];

    /// <summary>Sets the cool-down of channel <paramref name="channel"/>.</summary>
    public void SetCooldown(int channel, int value) => cooldowns[channel] = Wrap.Signed16(value);

    internal void Exchange(StateExchange s)
    {
        Enabled = s.Flag(Enabled);
        for (int i = 0; i < Channels; i++)
        {
            cooldowns[i] = s.Value(cooldowns[i]);
        }
    }
}
