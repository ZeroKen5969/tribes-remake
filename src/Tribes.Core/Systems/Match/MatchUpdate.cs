using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>
/// A match's part of each frame, in place of the campaign's story: every human-ruled tribe whose
/// chief is gone is asked to name a new one, tribes that are wiped out (or gave up) go out, and
/// once no two tribes still in are enemies the match is decided and its end countdown starts.
/// </summary>
internal sealed class MatchUpdate
{
    /// <summary>Frames without fighting before a player is asked for a new chief (as in the campaign).</summary>
    private const int QuietFrames = 50;

    private readonly GameWorld world;
    private readonly HintSystem hints;
    private readonly Action<GameEvent> raise;

    /// <summary>Runs the match of <paramref name="world"/>.</summary>
    public MatchUpdate(GameWorld world, HintSystem hints, Action<GameEvent> raise)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.raise = raise ?? throw new ArgumentNullException(nameof(raise));
    }

    /// <summary>One frame of the match (after the units, with the frame's story part).</summary>
    public void Advance()
    {
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Controller.IsHumanRuled && tribe.OutAtFrame < 0)
            {
                CheckChief(tribe);
            }
        }
        if (world.Match.IsDecided)
        {
            return;
        }
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.OutAtFrame < 0 && TribeStanding.IsOut(world, tribe.Index))
            {
                GoOut(tribe);
            }
        }
        Decide();
    }

    /// <summary>
    /// <paramref name="tribe"/> leaves the match now (wiped out, or its player gave up): it gets no
    /// orders and no AI from then on, its units keep their own behaviour.
    /// </summary>
    public void GoOut(TribeState tribe)
    {
        ArgumentNullException.ThrowIfNull(tribe);
        if (tribe.OutAtFrame >= 0)
        {
            return;
        }
        tribe.OutAtFrame = world.Frame;
        raise(new TribeOut(tribe.Index, tribe.Controller.Kind == TribeControl.Forfeited));
    }

    /// <summary>Decides the match once no two tribes still in it are enemies (when the match ends with its last side).</summary>
    public void Decide()
    {
        if (world.Match.IsDecided || !world.Match.EndsWithLastSide || world.EndCountdown != 0)
        {
            return;
        }
        List<int> inMatch = MatchStandings.StillIn(world);
        foreach (int a in inMatch)
        {
            foreach (int b in inMatch)
            {
                if (TribeRoles.AreEnemies(world, a, b))
                {
                    return;
                }
            }
        }
        world.Match.DecidedAtFrame = world.Frame;
        world.EndCountdown = MissionOutcome.VictoryStart;
        raise(new MatchDecided(inMatch));
    }

    /// <summary>
    /// When the tribe's chief is gone (dead or no adult), the tribe has a living adult and the game
    /// is quiet, its player is asked to name a new chief (the first adult is the hint's subject).
    /// </summary>
    private void CheckChief(TribeState tribe)
    {
        if (tribe.AwaitingNewChief || world.EndCountdown != 0 || TribeRoles.FramesSinceCombat(world, tribe.Index) < QuietFrames
            || !ChiefIsGone(tribe))
        {
            return;
        }
        foreach (Unit unit in world.UnitsOf(tribe.Index))
        {
            if (unit.HitPoints > 0 && unit.Age >= UnitAges.Adult)
            {
                tribe.AwaitingNewChief = true;
                hints.Show(tribe.Index, StoryTriggers.NewChiefHint, unit.Id, unit.Id);
                return;
            }
        }
    }

    private bool ChiefIsGone(TribeState tribe)
    {
        int chief = tribe[TribeCounter.Chief];
        return chief < 1 || chief > world.UnitSlots || world.Unit(chief).HitPoints == 0 || world.Unit(chief).Age < UnitAges.Adult;
    }
}
