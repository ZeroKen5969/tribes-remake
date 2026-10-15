using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Match;

/// <summary>
/// A tribe's start camp: the buildings and units the original's player starts a custom map with,
/// relative to its first building (the chief's hut).
/// </summary>
/// <param name="Objects">The camp's objects, their cells relative to the camp's origin.</param>
/// <param name="Origin">Where the mission puts the player's camp (its first building).</param>
internal sealed record CampTemplate(IReadOnlyList<InitialObject> Objects, GridPoint Origin)
{
    /// <summary>The camp of mission <paramref name="mission"/>'s player (tribe 0's initial objects).</summary>
    public static CampTemplate Of(MissionRules mission, int player)
    {
        ArgumentNullException.ThrowIfNull(mission);
        GridPoint? origin = null;
        var objects = new List<InitialObject>();
        foreach (InitialObject item in mission.InitialObjects)
        {
            if (item.Kind == InitialObjectKind.Animal || item.Tribe != player)
            {
                continue;
            }
            origin ??= item.Kind == InitialObjectKind.Building ? item.Cell : null;
            objects.Add(item);
        }
        GridPoint at = origin ?? (objects.Count > 0 ? objects[0].Cell : default);
        for (int i = 0; i < objects.Count; i++)
        {
            objects[i] = objects[i] with { Cell = new GridPoint(objects[i].Cell.X - at.X, objects[i].Cell.Y - at.Y) };
        }
        return new CampTemplate(objects, at);
    }
}

/// <summary>
/// Where camps go: on open ground (by the map's tiles) that is empty now, all in the largest area
/// units can walk across, at least <see cref="MinimumDistance"/> cells (between camp origins) from
/// every other camp and clear of their cells, as far from the nearest as can be; the first one as
/// near the mission's own player camp as fits. The places depend only on the map, so how many camps
/// a map has room for is a number of the map (<see cref="MatchStart.CampCapacity"/>). A match's
/// camps are then moved apart as far as the map allows (<see cref="Spread(List{GridPoint})"/>).
/// </summary>
internal sealed class CampPlacer
{
    /// <summary>The least distance between two camps' origins.</summary>
    public const int MinimumDistance = 12;

    /// <summary>The distance between camps' origins a map keeps when it has room (twice the least).</summary>
    public const int RoomyDistance = 2 * MinimumDistance;

    /// <summary>Rounds of <see cref="Spread(List{GridPoint})"/> at most.</summary>
    private const int SpreadRounds = 16;

    /// <summary>Layouts <see cref="Arrange"/> tries (from different first places).</summary>
    private const int ArrangeStarts = 8;

    /// <summary>Places <see cref="ChooseWithRoom"/> tries, the farthest first.</summary>
    private const int JoinTries = 32;

    private readonly GameWorld world;
    private readonly CampTemplate template;
    private readonly List<GridPoint> cells = new();

    /// <summary>Places camps of <paramref name="template"/> in <paramref name="world"/>.</summary>
    public CampPlacer(GameWorld world, CampTemplate template)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.template = template ?? throw new ArgumentNullException(nameof(template));
        foreach (InitialObject item in template.Objects)
        {
            if (item.Kind == InitialObjectKind.Unit)
            {
                cells.Add(item.Cell);
                continue;
            }
            BuildingKind kind = world.Content.Rules.Buildings.Kinds[item.BuildingKind ?? 0];
            for (int y = 0; y < kind.Height; y++)
            {
                for (int x = 0; x < kind.Width; x++)
                {
                    cells.Add(new GridPoint(item.Cell.X + x, item.Cell.Y + y));
                }
            }
        }
    }

    /// <summary>The origin for one more camp away from <paramref name="others"/>, or null when no place fits.</summary>
    public GridPoint? Choose(IReadOnlyCollection<GridPoint> others) => Choose(Candidates(), others, MinimumDistance);

    /// <summary>
    /// The origin for one more camp of a running match: the place farthest from
    /// <paramref name="others"/> (camps and where tribes are now, at least <see cref="MinimumDistance"/>
    /// away) where the new camp has room to build (<see cref="CampRoom"/>) and every camp of
    /// <paramref name="camps"/> (the tribes' camps, by tribe) keeps its room; null when there is none
    /// among the <see cref="JoinTries"/> farthest places.
    /// </summary>
    public GridPoint? ChooseWithRoom(IReadOnlyList<GridPoint> camps, IReadOnlyCollection<GridPoint> others)
    {
        ArgumentNullException.ThrowIfNull(camps);
        ArgumentNullException.ThrowIfNull(others);
        long least = (long)MinimumDistance * MinimumDistance;
        var scored = new List<(GridPoint Place, long Score)>();
        foreach (GridPoint candidate in Candidates())
        {
            long score = long.MaxValue;
            foreach (GridPoint other in others)
            {
                score = Math.Min(score, SquaredDistance(candidate, other));
            }
            if (score >= least)
            {
                scored.Add((candidate, score));
            }
        }
        // The farthest first; places of equal distance in rows from the top.
        scored.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score)
            : a.Place.Y != b.Place.Y ? a.Place.Y.CompareTo(b.Place.Y) : a.Place.X.CompareTo(b.Place.X));
        for (int i = 0; i < scored.Count && i < JoinTries; i++)
        {
            var all = new List<GridPoint>(camps) { scored[i].Place };
            if (KeepsRoom(all, all.Count - 1))
            {
                return scored[i].Place;
            }
        }
        return null;
    }

    /// <summary>Whether every camp of <paramref name="camps"/> (all settled but the last one, <paramref name="added"/>) has room to build.</summary>
    private bool KeepsRoom(List<GridPoint> camps, int added)
    {
        long near = 4L * CampRoom.BuildReach(world) * CampRoom.BuildReach(world);
        for (int i = 0; i < camps.Count; i++)
        {
            if ((i == added || SquaredDistance(camps[i], camps[added]) <= near)
                && CampRoom.Count(world, camps, i, i == added ? -1 : i, CampRoom.Needed) < CampRoom.Needed)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Whether every camp of a new match's layout <paramref name="camps"/> has room to build.</summary>
    public bool HaveRoom(List<GridPoint> camps)
    {
        ArgumentNullException.ThrowIfNull(camps);
        for (int i = 0; i < camps.Count; i++)
        {
            if (CampRoom.Count(world, camps, i, -1, CampRoom.Needed) < CampRoom.Needed)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The origins of up to <paramref name="count"/> camps on the map as it is now, in the order they
    /// are chosen (each away from the ones before); fewer when the map has no room for more.
    /// </summary>
    public List<GridPoint> Layout(int count, int minimumDistance = MinimumDistance) =>
        Layout(Candidates(), count, minimumDistance, first: null);

    /// <summary>
    /// The camps of a new match: <see cref="Layout(int, int)"/> started from the mission's own player camp
    /// and from places spread over the map, each moved apart (<see cref="Spread(List{GridPoint})"/>); the one whose
    /// closest two camps are farthest apart.
    /// </summary>
    public List<GridPoint> Arrange(int count)
    {
        List<GridPoint> all = Candidates();
        List<GridPoint> best = Layout(all, count, MinimumDistance, first: null);
        Spread(best, all);
        long bestLeast = Least(best);
        for (int start = 1; start < ArrangeStarts && best.Count >= 2 && all.Count > 0; start++)
        {
            GridPoint first = all[(int)((long)start * all.Count / ArrangeStarts)];
            List<GridPoint> camps = Layout(all, count, MinimumDistance, first);
            if (camps.Count < best.Count)
            {
                continue;
            }
            Spread(camps, all);
            long least = Least(camps);
            if (least > bestLeast)
            {
                (best, bestLeast) = (camps, least);
            }
        }
        return best;
    }

    private List<GridPoint> Layout(List<GridPoint> all, int count, int minimumDistance, GridPoint? first)
    {
        var candidates = new List<GridPoint>(all);
        var camps = new List<GridPoint>();
        var taken = new HashSet<GridPoint>();
        GridPoint? next = first;
        while (camps.Count < count && (next ?? Choose(candidates, camps, minimumDistance)) is GridPoint place)
        {
            next = null;
            camps.Add(place);
            Take(place, taken);
            candidates.RemoveAll(candidate => Touches(candidate, taken));
        }
        return camps;
    }

    /// <summary>The squared distance between the closest two of <paramref name="camps"/> (the largest value with fewer than two).</summary>
    private static long Least(List<GridPoint> camps)
    {
        long least = long.MaxValue;
        for (int i = 0; i < camps.Count; i++)
        {
            least = Math.Min(least, NearestOther(camps, i));
        }
        return least;
    }

    /// <summary>
    /// Moves camps of <paramref name="camps"/> (a <see cref="Layout(int, int)"/>) apart as far as the map
    /// allows: round after round each camp in turn goes to the free place farthest from all the
    /// others when that is farther from its nearest neighbour than where it is (so the closest two
    /// camps only ever get farther apart), until no camp moves.
    /// </summary>
    public void Spread(List<GridPoint> camps) => Spread(camps, Candidates());

    private void Spread(List<GridPoint> camps, List<GridPoint> candidates)
    {
        ArgumentNullException.ThrowIfNull(camps);
        if (camps.Count < 2)
        {
            return;
        }
        for (int round = 0; round < SpreadRounds; round++)
        {
            bool moved = false;
            for (int i = 0; i < camps.Count; i++)
            {
                moved |= MoveApart(camps, i, NearestOther(camps, i), candidates);
            }
            if (!moved)
            {
                return;
            }
        }
    }

    /// <summary>The squared distance from camp <paramref name="index"/> to the nearest other camp.</summary>
    private static long NearestOther(List<GridPoint> camps, int index)
    {
        long nearest = long.MaxValue;
        for (int i = 0; i < camps.Count; i++)
        {
            if (i != index)
            {
                nearest = Math.Min(nearest, SquaredDistance(camps[i], camps[index]));
            }
        }
        return nearest;
    }

    /// <summary>Moves camp <paramref name="index"/> to the free place farthest from the others when that beats <paramref name="least"/>; whether it moved.</summary>
    private bool MoveApart(List<GridPoint> camps, int index, long least, List<GridPoint> candidates)
    {
        var others = new List<GridPoint>(camps);
        others.RemoveAt(index);
        var taken = new HashSet<GridPoint>();
        foreach (GridPoint other in others)
        {
            Take(other, taken);
        }
        GridPoint? best = null;
        long bestScore = least;
        foreach (GridPoint candidate in candidates)
        {
            long score = long.MaxValue;
            foreach (GridPoint other in others)
            {
                score = Math.Min(score, SquaredDistance(candidate, other));
            }
            if (score > bestScore && !Touches(candidate, taken))
            {
                (best, bestScore) = (candidate, score);
            }
        }
        if (best is not GridPoint place)
        {
            return false;
        }
        camps[index] = place;
        return true;
    }

    /// <summary>Adds the cells of a camp at <paramref name="origin"/> and the ring around them to <paramref name="taken"/>.</summary>
    private void Take(GridPoint origin, HashSet<GridPoint> taken)
    {
        foreach (GridPoint offset in cells)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    taken.Add(new GridPoint(origin.X + offset.X + dx, origin.Y + offset.Y + dy));
                }
            }
        }
    }

    /// <summary>Whether a camp at <paramref name="origin"/> would cover a cell of <paramref name="taken"/>.</summary>
    private bool Touches(GridPoint origin, HashSet<GridPoint> taken)
    {
        foreach (GridPoint offset in cells)
        {
            if (taken.Contains(new GridPoint(origin.X + offset.X, origin.Y + offset.Y)))
            {
                return true;
            }
        }
        return false;
    }

    private GridPoint? Choose(List<GridPoint> candidates, IReadOnlyCollection<GridPoint> others, int minimumDistance)
    {
        long least = (long)minimumDistance * minimumDistance;
        GridPoint? best = null;
        long bestScore = -1;
        foreach (GridPoint candidate in candidates)
        {
            long score = others.Count == 0 ? -SquaredDistance(candidate, template.Origin) : long.MaxValue;
            foreach (GridPoint other in others)
            {
                score = Math.Min(score, SquaredDistance(candidate, other));
            }
            if ((others.Count == 0 || score >= least) && (best is null || score > bestScore))
            {
                (best, bestScore) = (candidate, score);
            }
        }
        return best;
    }

    private static long SquaredDistance(GridPoint a, GridPoint b)
    {
        long dx = a.X - b.X, dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>The origins where the whole camp fits in the largest walkable area, in rows from the top.</summary>
    private List<GridPoint> Candidates()
    {
        int[] area = WalkableAreas(out int largest);
        GridSize size = world.Map.PlayableSize;
        var found = new List<GridPoint>();
        for (int y = 1; y < size.Height - 1; y++)
        {
            for (int x = 1; x < size.Width - 1; x++)
            {
                var origin = new GridPoint(x, y);
                if (Fits(origin, area, largest))
                {
                    found.Add(origin);
                }
            }
        }
        return found;
    }

    private bool Fits(GridPoint origin, int[] area, int largest)
    {
        int width = world.Map.PlayableSize.Width;
        foreach (GridPoint offset in cells)
        {
            var cell = new GridPoint(origin.X + offset.X, origin.Y + offset.Y);
            if (!MatchTerrain.IsGround(world, cell) || !world.Map.CellAt(cell).IsEmpty || area[(cell.Y * width) + cell.X] != largest)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The walkable areas of the part in play (empty ground cells joined across their eight
    /// neighbours), each cell's area number (0 for none); <paramref name="largest"/> is the biggest area.
    /// </summary>
    private int[] WalkableAreas(out int largest)
    {
        GridSize size = world.Map.PlayableSize;
        int[] area = new int[size.Width * size.Height];
        var sizes = new List<int> { 0 };
        var queue = new Queue<GridPoint>();
        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                if (area[(y * size.Width) + x] != 0 || !Walkable(new GridPoint(x, y)))
                {
                    continue;
                }
                int number = sizes.Count;
                sizes.Add(Flood(new GridPoint(x, y), number, area, queue));
            }
        }
        largest = 0;
        for (int i = 1; i < sizes.Count; i++)
        {
            if (sizes[i] > sizes[largest])
            {
                largest = i;
            }
        }
        return area;
    }

    private int Flood(GridPoint start, int number, int[] area, Queue<GridPoint> queue)
    {
        int width = world.Map.PlayableSize.Width;
        int count = 0;
        area[(start.Y * width) + start.X] = number;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            GridPoint cell = queue.Dequeue();
            count++;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var next = new GridPoint(cell.X + dx, cell.Y + dy);
                    if (world.Map.InPlay(next) && area[(next.Y * width) + next.X] == 0 && Walkable(next))
                    {
                        area[(next.Y * width) + next.X] = number;
                        queue.Enqueue(next);
                    }
                }
            }
        }
        return count;
    }

    private bool Walkable(GridPoint cell)
    {
        MapCell content = world.Map.CellAt(cell);
        return MatchTerrain.IsGround(world, cell) && (content.IsEmpty || content.Content is CellContent.Unit or CellContent.Animal);
    }
}
