using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Search;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Senses;

/// <summary>
/// Scouts (scouting skill above 3) hiding and being noticed. Every eight frames a scout counts as
/// unseen until an enemy unit or a manned enemy building within sight notices it; a good scout
/// spots enemy traps nearby, and the players' scouts find their enemies' alarm places. An unseen
/// scout idling without orders crouches down and hides; a hidden scout that is seen or gets busy
/// gets up again. Crouching and getting up take the scout's whole turn.
/// </summary>
internal sealed class UnitStealth
{
    /// <summary>Scouts with more than this skill hide and notice.</summary>
    public const int ScoutingThreshold = 3;

    /// <summary>The detection check runs when frame and slot agree on these bits.</summary>
    private const int CheckMask = 7;

    /// <summary>Spiral rounds searched for whoever might notice the scout.</summary>
    private const int DetectionRounds = 17;

    /// <summary>A hidden scout is noticed within 1 + (this - its skill) / 3 + the watcher's skill / (its skill - 3) cells.</summary>
    private const int HiddenSightBase = 10, HiddenSightDivisor = 3;

    /// <summary>Scouts of at least this skill spot traps within (skill - <see cref="TrapSpottingMargin"/>) cells.</summary>
    private const int TrapSpottingSkill = 6, TrapSpottingMargin = 4;

    /// <summary>A building's scouting value is kept above its range in <see cref="Building.RangeInfo"/>.</summary>
    private const int RangeMask = 0x0F, ScoutingShift = 4;

    /// <summary>Alarm places are found within (scouting - this) cells.</summary>
    private const int AlarmSpottingMargin = 3;

    /// <summary>An alarm offset of this many cells or more reads the distance table's first row or column.</summary>
    private const int AlarmTableReach = 10;

    /// <summary>The action of actors staged in a story scene; they hide like idle units.</summary>
    private const int StagedAction = 105;

    private readonly GameWorld world;
    private readonly SpiralSearch spiral;
    private readonly IReadOnlyList<AnimationDefinition> animations;
    private readonly IReadOnlyList<BuildingKind> kinds;
    private readonly IReadOnlyList<IReadOnlyList<int>> distanceTable;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitStealth(GameWorld world, SpiralSearch spiral)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
        animations = world.Content.Rules.Animations.Animations;
        kinds = world.Content.Rules.Buildings.Kinds;
        distanceTable = world.Content.Rules.Movement.DistanceAndFacing;
    }

    /// <summary>
    /// The stealth step of the turn of scout <paramref name="turn"/>'s unit; returns true when
    /// crouching or getting up takes the rest of its turn.
    /// </summary>
    public bool Update(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        Unit unit = turn.Unit;
        if ((world.Frame & CheckMask) == (unit.Id & CheckMask))
        {
            unit.Status |= UnitStatus.Undetected;
            turn.ClearThreat();
            Scan(unit);
            if (TribeRoles.IsPlayer(world, unit.Tribe))
            {
                SpotAlarms(unit);
            }
        }
        int animation = unit.AnimationNumber;
        if (animation is UnitAnimations.Hide or UnitAnimations.Unhide)
        {
            unit.AnimationTick++;
            if (unit.AnimationTick < animations[animation].FrameCount)
            {
                return true;
            }
            if (animation == UnitAnimations.Hide)
            {
                unit.Status |= UnitStatus.Hidden;
            }
            return false;
        }
        UnitStatus seen = unit.Status & (UnitStatus.Undetected | UnitStatus.Hidden);
        if (unit.Action == ActionNumbers.Idle && unit.AssignedTask == ActionNumbers.Idle && unit.RunBonus == 0
            && seen == UnitStatus.Undetected)
        {
            Play(unit, UnitAnimations.Hide);
            return true;
        }
        bool staysHidden = (unit.Action == ActionNumbers.Idle || unit.Action == StagedAction)
            && (unit.Status & UnitStatus.Undetected) != 0 && unit.RunBonus <= 0;
        if ((unit.Status & UnitStatus.Hidden) == 0 || staysHidden)
        {
            return false;
        }
        Play(unit, UnitAnimations.Unhide);
        unit.Status &= ~UnitStatus.Hidden;
        return true;
    }

    private static void Play(Unit unit, int animation)
    {
        unit.Animation = animation;
        unit.AnimationTick = 0;
    }

    /// <summary>
    /// Looks for whoever notices <paramref name="scout"/>; spots enemy traps on the way (a spotted
    /// trap of a computer tribe counts as no longer set) and records who found them.
    /// </summary>
    private void Scan(Unit scout)
    {
        spiral.Visit(new GridPoint(scout.X, scout.Y), DetectionRounds, cell =>
        {
            MapCell content = world.Map.CellAt(cell.Cell);
            if (NoticedBy(scout, content, cell))
            {
                scout.Status &= ~UnitStatus.Undetected;
                return true;
            }
            if (content.Content == CellContent.Trap && TribeRoles.AreEnemies(world, content.Owner, scout.Tribe)
                && scout.ScoutingSkill >= TrapSpottingSkill && cell.Distance <= scout.ScoutingSkill - TrapSpottingMargin)
            {
                world.Map.SetCell(cell.Cell, new MapCell(CellContent.SpottedTrap, content.Index, Finder(scout)));
                if (TribeRoles.IsComputer(world, content.Owner))
                {
                    world.Tribes[content.Owner].Ai.Flags--;
                }
            }
            return false;
        });
    }

    private bool NoticedBy(Unit scout, MapCell content, SpiralCell cell)
    {
        bool hidden = (scout.Status & UnitStatus.Hidden) != 0;
        if (content.Content == CellContent.Unit && IsEnemy(scout, world.TribeOfUnit(content.Index)))
        {
            Unit watcher = world.Unit(content.Index);
            int reach = hidden ? HiddenReach(scout, watcher.ScoutingSkill) : watcher.SightRadius;
            return watcher.HitPoints > 0 && reach >= cell.Distance;
        }
        if (content.Content == CellContent.Building && IsEnemy(scout, world.TribeOfBuilding(content.Index)))
        {
            Building post = world.Building(content.Index);
            BuildingKind kind = kinds[post.Kind];
            int reach = hidden ? HiddenReach(scout, post.RangeInfo >> ScoutingShift) : post.RangeInfo & RangeMask;
            return post.HitPoints > 0 && post.OccupantCount != 0
                && cell.Cell.X == post.X + (kind.Width / 2) && cell.Cell.Y == post.Y + (kind.Height / 2)
                && reach >= cell.Distance;
        }
        return false;
    }

    /// <summary>How far a watcher with <paramref name="watcherScouting"/> notices hidden <paramref name="scout"/>.</summary>
    private static int HiddenReach(Unit scout, int watcherScouting) =>
        1 + ((HiddenSightBase - scout.ScoutingSkill) / HiddenSightDivisor)
        + (watcherScouting / (scout.ScoutingSkill - ScoutingThreshold));

    private bool IsEnemy(Unit scout, int tribe) => TribeRoles.AreEnemies(world, scout.Tribe, tribe);

    /// <summary>
    /// The tribe a trap <paramref name="scout"/> spots is shown to: in a match the scout's own; in
    /// the campaign the player, who is shown every spotted trap as in the original (its codes do
    /// not tell who found them).
    /// </summary>
    private int Finder(Unit scout) => world.Mode == WorldMode.Match ? scout.Tribe : TribeRoles.Player(world);

    /// <summary>A player's scout notices the alarm places of its tribe's enemies within its skill.</summary>
    private void SpotAlarms(Unit scout)
    {
        foreach (int enemy in TribeRoles.EnemiesOf(world, scout.Tribe))
        {
            foreach (AlarmCall alarm in world.Tribes[enemy].Alarms)
            {
                if (alarm.Age < 0 || alarm.IsKnownTo(scout.Tribe))
                {
                    continue;
                }
                int measured = distanceTable[AlarmIndex(alarm.Y - scout.Y)][AlarmIndex(alarm.X - scout.X)];
                if (measured / GridDistances.FacingDigits <= scout.ScoutingSkill - AlarmSpottingMargin)
                {
                    alarm.MarkKnownTo(scout.Tribe);
                }
            }
        }
    }

    private static int AlarmIndex(int delta) =>
        delta > -AlarmTableReach && delta < AlarmTableReach ? GridDistances.Reach + delta : 0;
}
