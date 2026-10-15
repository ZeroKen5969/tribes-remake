using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Paths;
using Tribes.Core.Systems.Units.Animation;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// A unit noticing a threat: the most important enemy, building or animal within its sight, and
/// what it does about it (attack, shoot from a tower, take cover, call its tribe mates).
/// </summary>
internal sealed partial class ThreatWatch
{
    /// <summary>Missions whose story keeps the units from looking for threats at some objectives.</summary>
    private const int RaidMission = 3, RaidQuietFrom = 5, VillageMission = 2, VillageQuietObjective = 1;

    /// <summary>In the raid mission only these entries of the threat search order apply.</summary>
    private const int RaidOrderRow = 2, SkippedPlayerGroup = 1;

    /// <summary>The last row of the threat search order that belongs to a tribe of its number.</summary>
    private const int LastOwnRow = 1;

    /// <summary>Threat priorities (<see cref="ObjectGroup.ThreatPriority"/>; an enemy child counts as 1).</summary>
    private const int ChildThreat = 1, AnimalThreat = 2, UnitThreat = 4;

    /// <summary>A unarmed unit takes cover in the player's first building when the threat is this near it.</summary>
    private const int ShelterReach = 6;

    /// <summary>Bare hands.</summary>
    private const int BareHands = 9;

    /// <summary>The highest action a unit inside a building pops out to its corner for.</summary>
    private const int LastPopOutAction = 82;

    /// <summary>The animation of a unit running for cover.</summary>
    private const int RunForCover = -95;

    /// <summary>The player is told about an enemy (hint 4) and warned once (notice 6 on channel 0).</summary>
    private const int EnemyHint = 4, EnemyNotice = 6, EnemyNoticeChannel = 0;

    /// <summary>Story objectives at or above this are playing their scene.</summary>
    private const int ScenePlaying = 50;

    private readonly GameWorld world;
    private readonly SectorSearch sectors;
    private readonly RouteFinder routes;
    private readonly TargetDistances distances;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly HintSystem hints;
    private readonly ActionAnimation animation;
    private readonly StoryEncounters encounters;
    private readonly TargetCycle targets;
    private readonly IReadOnlyList<ActionDefinition> definitions;

    /// <summary>
    /// The groups each tribe watches, kept while nothing they depend on changes (the frame, the
    /// tribes and their relations): every unit of a tribe watches the same groups.
    /// </summary>
    private readonly Dictionary<int, (WatchStamp Stamp, List<List<int>> Sets)> watchedByTribe = new();

    /// <summary>Lets the units of <paramref name="world"/> notice threats.</summary>
    public ThreatWatch(GameWorld world, SectorSearch sectors, RouteFinder routes, TargetDistances distances,
        UnitActions actions, UnitOrders orders, HintSystem hints, ActionAnimation animation, StoryEncounters encounters,
        TargetCycle targets)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.sectors = sectors ?? throw new ArgumentNullException(nameof(sectors));
        this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.animation = animation ?? throw new ArgumentNullException(nameof(animation));
        this.encounters = encounters ?? throw new ArgumentNullException(nameof(encounters));
        this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
        definitions = world.Content.Rules.Actions.Actions;
    }

    /// <summary>
    /// The turn's unit looks for the most important threat within its sight (the groups its tribe
    /// watches, in order, until one of the most important kind is found) and responds to it.
    /// An attacker in the regular decision keeps its target unless the threat is more important than
    /// its action's threshold, or it is out of reach of its target and the threat is in reach.
    /// Returns whether a threat was found.
    /// </summary>
    public bool Look(UnitTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        if ((world.Mission == RaidMission && world.Script.ObjectiveStage >= RaidQuietFrom)
            || (world.Mission == VillageMission && world.Script.ObjectiveStage == VillageQuietObjective))
        {
            return false;
        }
        Unit unit = turn.Unit;
        var at = new GridPoint(unit.X, unit.Y);
        TargetSearch search = turn.Threat;
        sectors.Restart(search, at, unit.SightRadius);
        foreach (List<int> watched in WatchedGroups(unit.Tribe))
        {
            foreach (int group in watched)
            {
                sectors.SearchGroup(search, group, unit, at);
            }
            if (watched.Count > 0 && search.Priority == sectors.Groups[watched[0]].ThreatPriority)
            {
                break;
            }
        }
        if (search.Priority <= 0)
        {
            return false;
        }
        if (turn.RegularDecision && unit.Action <= ActionNumbers.LastBasicAttack)
        {
            int range = routes.ActionRange(unit);
            turn.MayActFreely = turn.MayActFreely
                && (definitions[unit.Action].ThreatThreshold < search.Priority || (turn.GoalCells > range && search.Distance <= range));
        }
        Respond(turn);
        return true;
    }

    /// <summary>
    /// The object groups <paramref name="tribe"/> watches for threats, in order, as sets searched
    /// whole before the next: the content's row for the tribe (a tribe after the first two uses
    /// the second tribe's), where a group of the other tribe's units or buildings stands for the
    /// units or buildings of every enemy of the tribe.
    /// </summary>
    private List<List<int>> WatchedGroups(int tribe)
    {
        var stamp = new WatchStamp(world.Frame, world.Tribes.Count, world.Relations.Version, world.Mission);
        if (watchedByTribe.TryGetValue(tribe, out (WatchStamp Stamp, List<List<int>> Sets) known) && known.Stamp == stamp)
        {
            return known.Sets;
        }
        List<List<int>> sets = known.Sets ?? new List<List<int>>();
        WorkOutWatchedGroups(tribe, sets);
        watchedByTribe[tribe] = (stamp, sets);
        return sets;
    }

    /// <summary>What the watched groups of a tribe depend on.</summary>
    private readonly record struct WatchStamp(int Frame, int Tribes, int Relations, int Mission);

    /// <summary>Fills <paramref name="sets"/> (its lists are used again) with the groups <paramref name="tribe"/> watches.</summary>
    private void WorkOutWatchedGroups(int tribe, List<List<int>> sets)
    {
        IReadOnlyList<IReadOnlyList<int>> order = world.Content.Rules.Targets.ThreatSearchOrder;
        bool raid = world.Mission == RaidMission;
        bool player = TribeRoles.IsPlayer(world, tribe);
        IReadOnlyList<int> row = order[raid && !player ? RaidOrderRow : Math.Min(tribe, LastOwnRow)];
        int used = 0;
        for (int i = 0; i < row.Count; i++)
        {
            if (!raid || !player || i != SkippedPlayerGroup)
            {
                if (used == sets.Count)
                {
                    sets.Add(new List<int>());
                }
                EnemyGroups(tribe, row[i], sets[used++]);
            }
        }
        sets.RemoveRange(used, sets.Count - used);
    }

    /// <summary>Fills <paramref name="groups"/> with the groups an entry of a watch row stands for, for <paramref name="tribe"/>.</summary>
    private void EnemyGroups(int tribe, int entry, List<int> groups)
    {
        groups.Clear();
        ObjectGroupKind kind = ObjectGroupTable.KindOf(entry);
        if (kind == ObjectGroupKind.Animals)
        {
            groups.Add(entry);
            return;
        }
        foreach (int enemy in TribeRoles.EnemiesOf(world, tribe))
        {
            groups.Add(kind == ObjectGroupKind.Units ? ObjectGroupTable.Units(enemy) : ObjectGroupTable.Buildings(enemy));
        }
    }
}
