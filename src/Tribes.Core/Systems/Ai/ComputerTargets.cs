using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>
/// The enemy a computer tribe goes for in a match and the places that depend on it: the nearest
/// enemy still in the match and, among those not more than half as far again, the one with the
/// fewest living adults (then the nearer, then the lower number); never an ally. In an endless
/// world a human-ruled enemy within the rules' reach comes first, however near the other enemies
/// are: of several, the one the fewest other computers attack (then the nearer, then the lower
/// number), so the attacks spread over the people around. The mission's
/// attack routes and trap and spell areas, made for the way from the computer's village to the
/// player's camp, are laid onto the way from the computer's camp to that enemy.
/// </summary>
internal sealed class ComputerTargets
{
    /// <summary>An enemy counts as near as the nearest while its distance is within 3/2 of it (compared squared).</summary>
    private const int NearNumerator = 9, NearDenominator = 4;

    /// <summary>Spiral rounds searched for ground under a waypoint.</summary>
    private const int WaypointRounds = 15;

    private readonly GameWorld world;
    private readonly ComputerPlans plans;

    /// <summary>Chooses targets in <paramref name="world"/>.</summary>
    public ComputerTargets(GameWorld world, ComputerPlans plans)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.plans = plans ?? throw new ArgumentNullException(nameof(plans));
    }

    /// <summary>The enemy <paramref name="tribe"/>'s computer goes for now, or -1 when no enemy is left.</summary>
    public int Choose(int tribe)
    {
        GridPoint home = plans.For(tribe).Home;
        long peopleReach = PeopleReach();
        var candidates = new List<(int Tribe, long Distance, int Adults)>();
        long nearest = long.MaxValue;
        int person = -1;
        (int Attackers, long Distance) personKey = (int.MaxValue, long.MaxValue);
        foreach (int enemy in TribeRoles.EnemiesOf(world, tribe))
        {
            if (TribeStanding.IsOut(world, enemy))
            {
                continue;
            }
            long distance = SquaredDistance(home, TribeStanding.Seat(world, enemy));
            if (!Endless.EndlessPacing.MayTarget(world, tribe, enemy, distance))
            {
                continue;
            }
            if (distance <= peopleReach && TribeRoles.IsPlayer(world, enemy))
            {
                int attackers = Endless.EndlessPacing.Attackers(world, tribe, enemy);
                if (attackers < personKey.Attackers || (attackers == personKey.Attackers && distance < personKey.Distance))
                {
                    person = enemy;
                    personKey = (attackers, distance);
                }
            }
            candidates.Add((enemy, distance, TribeStanding.LivingAdults(world, enemy)));
            nearest = Math.Min(nearest, distance);
        }
        if (person >= 0)
        {
            return person;
        }
        int best = -1;
        (long Distance, int Adults) bestKey = (long.MaxValue, int.MaxValue);
        foreach ((int enemy, long distance, int adults) in candidates)
        {
            if (distance * NearDenominator > nearest * NearNumerator)
            {
                continue;
            }
            if (adults < bestKey.Adults || (adults == bestKey.Adults && distance < bestKey.Distance))
            {
                best = enemy;
                bestKey = (distance, adults);
            }
        }
        return best;
    }

    /// <summary>
    /// The waypoints of attack route <paramref name="route"/> of the mission from
    /// <paramref name="tribe"/>'s camp to <paramref name="target"/>: each waypoint keeps its place
    /// relative to the way from the mission's computer home to its player camp, turned and scaled
    /// onto the way from the camp to the target, on the nearest ground.
    /// </summary>
    public List<GridPoint> Route(int tribe, int target, int route)
    {
        MissionAi template = plans.Template;
        GridPoint from = plans.For(tribe).Home;
        GridPoint to = TribeStanding.Seat(world, target);
        var waypoints = new List<GridPoint>();
        foreach (GridPoint waypoint in template.AttackRoutes[route])
        {
            GridPoint laid = MatchTerrain.Inside(world, Lay(waypoint, template.Home, plans.TemplateEnemyCamp(), from, to));
            waypoints.Add(MatchTerrain.NearestGround(world, laid, WaypointRounds) ?? laid);
        }
        return waypoints;
    }

    /// <summary>
    /// The mission's areas around the player camp (traps, spells) moved to <paramref name="target"/>
    /// (unchanged in the campaign).
    /// </summary>
    public IReadOnlyList<CellRect> Areas(IReadOnlyList<CellRect> areas, int target)
    {
        ArgumentNullException.ThrowIfNull(areas);
        if (world.Mode == WorldMode.Campaign || target < 0)
        {
            return areas;
        }
        GridPoint camp = plans.TemplateEnemyCamp();
        GridPoint seat = TribeStanding.Seat(world, target);
        var moved = new List<CellRect>(areas.Count);
        foreach (CellRect area in areas)
        {
            GridPoint topLeft = MatchTerrain.Inside(world, new GridPoint(area.Left - camp.X + seat.X, area.Top - camp.Y + seat.Y));
            GridPoint bottomRight = MatchTerrain.Inside(world, new GridPoint(area.Right - camp.X + seat.X, area.Bottom - camp.Y + seat.Y));
            moved.Add(new CellRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y));
        }
        return moved;
    }

    /// <summary>
    /// <paramref name="point"/> laid from the way <paramref name="fromTemplate"/> → <paramref name="toTemplate"/>
    /// onto the way <paramref name="from"/> → <paramref name="to"/> (a turn and a scale about the start, as complex numbers).
    /// </summary>
    private static GridPoint Lay(GridPoint point, GridPoint fromTemplate, GridPoint toTemplate, GridPoint from, GridPoint to)
    {
        long dx = toTemplate.X - fromTemplate.X, dy = toTemplate.Y - fromTemplate.Y;
        long ex = to.X - from.X, ey = to.Y - from.Y;
        long qx = point.X - fromTemplate.X, qy = point.Y - fromTemplate.Y;
        long norm = (dx * dx) + (dy * dy);
        if (norm == 0)
        {
            return new GridPoint(from.X + (int)qx, from.Y + (int)qy);
        }
        long a = (ex * dx) + (ey * dy), b = (ey * dx) - (ex * dy);
        return new GridPoint(from.X + Divide((qx * a) - (qy * b), norm), from.Y + Divide((qx * b) + (qy * a), norm));
    }

    /// <summary><paramref name="value"/> / <paramref name="divisor"/> rounded to the nearest whole number (halves away from zero).</summary>
    private static int Divide(long value, long divisor) =>
        (int)((value >= 0 ? value + (divisor / 2) : value - (divisor / 2)) / divisor);

    /// <summary>
    /// How near (squared cells) a human-ruled enemy must be to come first: the endless rules' reach,
    /// or -1 (never) in other worlds and when the rules turn it off.
    /// </summary>
    private long PeopleReach()
    {
        if (world.Endless == null)
        {
            return -1;
        }
        long reach = world.Content.Endless!.Pacing.HumanReachCells;
        return reach > 0 ? reach * reach : -1;
    }

    private static long SquaredDistance(GridPoint a, GridPoint b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }
}
