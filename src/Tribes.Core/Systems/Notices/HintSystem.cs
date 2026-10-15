using System;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Notices;

/// <summary>
/// Hints and notices for the players. A hint is shown at most once per cool-down of its channel,
/// from its first mission on, and (except the hints about something out of sight) only after a
/// quiet spell without fighting. Each human-ruled tribe's player has its own channels and option
/// (<see cref="TribeRoles.Hints"/>); hints and notices about a computer tribe go to no one.
/// </summary>
internal sealed class HintSystem
{
    /// <summary>Hints about something the player may not be looking at; they do not wait for quiet.</summary>
    private const int OffScreenHint = 4, OffScreenAttackHint = 5;

    /// <summary>Hints from this number on are shown whatever the player's option and the game's state.</summary>
    private const int FirstCompulsoryHint = 18;

    /// <summary>Frames without fighting before ordinary hints are shown.</summary>
    private const int QuietFrames = 50;

    /// <summary>The notice kind of the original's "advance the notices" call, which is presentation only.</summary>
    private const int AdvanceNotices = 100;

    /// <summary>The tribe of a hint about a unit slot there is not: nobody's.</summary>
    private const int NoTribe = -1;

    private readonly GameWorld world;
    private readonly Action<GameEvent> raise;

    /// <summary>Shows hints of <paramref name="world"/> through <paramref name="raise"/>.</summary>
    public HintSystem(GameWorld world, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
    }

    /// <summary>
    /// One frame of the hint channels' cool-downs: a used channel counts up and is free again after
    /// its cool-down. While a player has hints switched off every channel stays blocked. In the
    /// campaign the player's channels count; in a match every human-ruled tribe's.
    /// </summary>
    public void UpdateCooldowns()
    {
        if (world.Mode == WorldMode.Campaign)
        {
            UpdateCooldowns(world.Hints);
            return;
        }
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Controller.IsHumanRuled)
            {
                UpdateCooldowns(tribe.Hints);
            }
        }
    }

    /// <summary>
    /// Offers hint <paramref name="hint"/> about unit <paramref name="subject"/> to its tribe's player
    /// (0 for none: to the campaign's player); hints about units of a computer tribe are dropped.
    /// </summary>
    public void Offer(int hint, int subject, int target)
    {
        int tribe = subject <= 0 ? TribeRoles.Player(world) : subject <= world.UnitSlots ? world.TribeOfUnit(subject) : NoTribe;
        Offer(tribe, hint, subject, target);
    }

    /// <summary>
    /// Offers hint <paramref name="hint"/> to the player of <paramref name="tribe"/>, about its unit
    /// <paramref name="subject"/> (0 for none): when its channel is free the channel is used, and
    /// the hint is shown if the game has been quiet long enough (hints about things out of sight do
    /// not wait).
    /// </summary>
    public void Offer(int tribe, int hint, int subject, int target)
    {
        if (!TribeRoles.IsPlayer(world, tribe)
            || (subject > 0 && (subject > world.UnitSlots || world.TribeOfUnit(subject) != tribe)))
        {
            return;
        }
        Hint definition = world.Content.Rules.Story.Hints[hint];
        HintState hints = TribeRoles.Hints(world, tribe);
        if (world.Mission < definition.MinMission || hints.Cooldown(definition.Channel) >= 0)
        {
            return;
        }
        hints.SetCooldown(definition.Channel, 0);
        bool outOfSight = hint == OffScreenHint || hint == OffScreenAttackHint;
        if (outOfSight || TribeRoles.FramesSinceCombat(world, tribe) >= QuietFrames)
        {
            Show(tribe, hint, subject, target);
        }
    }

    /// <summary>Shows hint <paramref name="hint"/> to the campaign's player (see <see cref="Show(int, int, int, int)"/>).</summary>
    public bool Show(int hint, int subject, int target) => Show(TribeRoles.Player(world), hint, subject, target);

    /// <summary>
    /// Shows hint <paramref name="hint"/> to the player of <paramref name="tribe"/> unless that
    /// player switched hints off, a story scene is playing, the player must name a new chief or the
    /// mission is ending (compulsory hints always show); returns whether it was shown.
    /// </summary>
    public bool Show(int tribe, int hint, int subject, int target)
    {
        bool shown = hint >= FirstCompulsoryHint
            || (TribeRoles.Hints(world, tribe).Enabled && !world.Script.Active && !TribeRoles.AwaitsNewChief(world, tribe)
                && world.EndCountdown <= 0);
        if (shown)
        {
            if (world.Mode == WorldMode.Campaign)
            {
                world.Script.FocusUnit = subject;
            }
            raise(new HintEvent(hint, subject, target) { Tribe = tribe });
        }
        return shown;
    }

    /// <summary>Adds a notice to the campaign player's message list (see <see cref="Notice(int, int, int, int, int)"/>).</summary>
    public void Notice(int kind, int x, int y, int value) => Notice(TribeRoles.Player(world), kind, x, y, value);

    /// <summary>
    /// Adds a notice to the message list of <paramref name="tribe"/>'s player (not at the very start
    /// of a mission, nor while it ends; not for a computer tribe).
    /// </summary>
    public void Notice(int tribe, int kind, int x, int y, int value)
    {
        if (kind != AdvanceNotices && world.Frame != 0 && world.EndCountdown <= 0 && TribeRoles.IsPlayer(world, tribe))
        {
            raise(new NoticeEvent(kind, x, y, value) { Tribe = tribe });
        }
    }

    private void UpdateCooldowns(HintState hints)
    {
        var periods = world.Content.Rules.Story.HintChannelCooldowns;
        for (int channel = 0; channel < HintState.Channels; channel++)
        {
            if (!hints.Enabled)
            {
                hints.SetCooldown(channel, periods[channel]);
                continue;
            }
            if (hints.Cooldown(channel) >= 0)
            {
                hints.SetCooldown(channel, hints.Cooldown(channel) + 1);
            }
            if (hints.Cooldown(channel) >= periods[channel])
            {
                hints.SetCooldown(channel, HintState.Free);
            }
        }
    }
}
