using System;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// When the computer tribes of a match or an endless world attack. In both, no computer attacks
/// anyone during the opening grace (<see cref="WorldSettings.OpeningGraceFrames"/>): no waves, no
/// chasing of intruders, and its ramp-up only starts once the grace is over; a computer defends only
/// the land around its own camp (half the camp spacing), not the side of the original map its plan
/// came from; a tribe that joins later is left alone for a while and a computer that joins waits as
/// long (a match: the opening grace's length). An endless world adds (<see cref="EndlessPacingRules"/>):
/// its own newcomer grace, a computer goes only for enemies
/// within reach and not much stronger than it, only so many computers go for the same tribe at
/// once, and the computers' timetables are shifted by tribe. The campaign keeps the original rules.
/// </summary>
internal static class EndlessPacing
{
    private const int Percent = 100;

    /// <summary>Frames between the timetables of two tribes in a row (odd, so 64 tribes in a row never share a phase of a power-of-two cycle).</summary>
    private const int PhaseStep = 7;

    /// <summary>
    /// The frame count the periodic checks of the computer of <paramref name="tribe"/> go by
    /// (intruders, waves under way, the build plan, armoury orders, spells): in an endless world
    /// each tribe's timetable is shifted by its number, so the many computer tribes take turns
    /// instead of all checking on the same frame; other worlds keep the original's frame.
    /// </summary>
    public static int Clock(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Endless == null ? world.Frame : world.Frame + (tribe * PhaseStep);
    }

    /// <summary>
    /// The frames the computer of <paramref name="tribe"/> counts its ramp-up and waves by: in a
    /// match, from the end of the opening grace; in an endless world, from the later of that and its
    /// tribe's arrival.
    /// </summary>
    public static int ComputerFrames(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Mode != WorldMode.Match)
        {
            return world.Frame;
        }
        int from = world.Settings.OpeningGraceFrames;
        if (world.Endless != null)
        {
            from = Math.Max(from, world.Tribes[tribe].ArrivedAtFrame);
        }
        return Math.Max(0, world.Frame - from);
    }

    /// <summary>Whether the opening grace of a match or endless world is still on.</summary>
    public static bool InOpeningGrace(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Mode == WorldMode.Match && world.Frame < world.Settings.OpeningGraceFrames;
    }

    /// <summary>Whether the computer of <paramref name="tribe"/> may send a wave now (not in the opening grace; a newcomer waits out its own).</summary>
    public static bool MaySendWaves(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return !InOpeningGrace(world) && !InNewcomerGrace(world, tribe);
    }

    /// <summary>Whether the computer of <paramref name="tribe"/> may go after intruders on its land now (as <see cref="MaySendWaves"/>).</summary>
    public static bool MayDefend(GameWorld world, int tribe) => MaySendWaves(world, tribe);

    /// <summary>
    /// The land the computer of <paramref name="tribe"/> defends: the alarm area of its plan
    /// (<paramref name="planned"/>), in a match or an endless world kept to half the camp spacing
    /// around its camp (the original's area is the computer's whole half of a two-tribe map).
    /// </summary>
    public static CellRect DefendedLand(GameWorld world, int tribe, CellRect planned)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Mode != WorldMode.Match)
        {
            return planned;
        }
        int reach = Match.CampLand.Reach(world);
        Content.GridPoint camp = world.Tribes[tribe].Camp;
        return new CellRect(Math.Max(planned.Left, camp.X - reach), Math.Max(planned.Top, camp.Y - reach),
            Math.Min(planned.Right, camp.X + reach), Math.Min(planned.Bottom, camp.Y + reach));
    }

    /// <summary>
    /// Whether the computer of <paramref name="attacker"/>, at squared distance
    /// <paramref name="squaredDistance"/> from <paramref name="target"/>, may go for it.
    /// </summary>
    public static bool MayTarget(GameWorld world, int attacker, int target, long squaredDistance)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (InOpeningGrace(world) || InNewcomerGrace(world, target))
        {
            return false;
        }
        if (world.Endless == null)
        {
            return true;
        }
        EndlessPacingRules rules = world.Content.Endless!.Pacing;
        if (squaredDistance > (long)rules.AttackReachCells * rules.AttackReachCells)
        {
            return false;
        }
        long own = Match.TribeStanding.LivingAdults(world, attacker);
        long theirs = Match.TribeStanding.LivingAdults(world, target);
        return theirs * Percent <= own * rules.StrengthPercent && Attackers(world, attacker, target) < rules.AttackersPerTarget;
    }

    /// <summary>
    /// Whether <paramref name="tribe"/> joined a match or an endless world after the start and is
    /// still within its grace time (an endless world's newcomer grace; a match's opening grace, from
    /// the tribe's arrival).
    /// </summary>
    private static bool InNewcomerGrace(GameWorld world, int tribe)
    {
        if (world.Mode != WorldMode.Match)
        {
            return false;
        }
        int arrived = world.Tribes[tribe].ArrivedAtFrame;
        int grace = world.Endless != null ? world.Content.Endless!.Pacing.NewcomerGraceFrames : world.Settings.OpeningGraceFrames;
        return arrived > 0 && world.Frame - arrived < grace;
    }

    /// <summary>How many computer tribes other than <paramref name="attacker"/> have a wave out against <paramref name="target"/>.</summary>
    internal static int Attackers(GameWorld world, int attacker, int target)
    {
        int count = 0;
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Index != attacker && tribe.Controller.RunsAi && tribe.Ai.WaveTarget >= 0 && tribe.Ai.WaveTribe == target)
            {
                count++;
            }
        }
        return count;
    }
}
