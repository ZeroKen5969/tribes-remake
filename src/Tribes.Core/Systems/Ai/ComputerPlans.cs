using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>
/// The plan each computer tribe follows: in the campaign the mission's own (posts, build plan,
/// home, alarm and territory areas at fixed places of the mission's map); in a match the
/// mission's plan moved to the tribe's camp — every place shifted so that the plan's home is the
/// camp, the planned buildings moved to the nearest ground that fits them (by terrain alone, so
/// the plan is the same whenever it is made) and the posts at planned buildings with them.
/// Places that look for the enemy (attack routes, trap and spell areas) follow the enemy the
/// computer goes for (ComputerTargets). A plan is kept while the land it reads stays as it was
/// (an endless world's land grows by chunks; land far from the camp does not count), so it always
/// follows from the saved world and a loaded copy makes the same one.
/// </summary>
internal sealed class ComputerPlans
{
    /// <summary>Spiral rounds searched for ground that fits a planned building.</summary>
    private const int RelocationRounds = 25;

    /// <summary>The mission's player tribe, whose camp the mission's plan was made against.</summary>
    private const int TemplatePlayer = 0;

    /// <summary>
    /// Cells around a plan's places that making it may read besides the search for room: the
    /// moves into the land in play, and the land's edges checked against those cells.
    /// </summary>
    private const int ClampReach = 2, EdgeReach = 3;

    private readonly GameWorld world;
    private readonly Dictionary<int, MadePlan> cache = new();

    /// <summary>Makes the plans of <paramref name="world"/>'s computer tribes.</summary>
    public ComputerPlans(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>The mission's own plan.</summary>
    public MissionAi Template => world.Content.Rules.Missions.Missions[world.Mission].Ai;

    /// <summary>The plan of <paramref name="tribe"/>'s computer.</summary>
    public MissionAi For(int tribe)
    {
        MissionAi template = Template;
        if (world.Mode == WorldMode.Campaign)
        {
            return template;
        }
        GridPoint camp = world.Tribes[tribe].Camp;
        int chunks = world.Map.Chunks.Count;
        if (cache.TryGetValue(tribe, out MadePlan? made) && made.Mission == world.Mission && made.Camp == camp)
        {
            if (made.Chunks == chunks)
            {
                return made.Plan;
            }
            if (LandWithin(made.Reach) == made.Land)
            {
                cache[tribe] = made with { Chunks = chunks };
                return made.Plan;
            }
        }
        GridRect reach = Reach(template, camp);
        MissionAi plan = Move(template, camp);
        cache[tribe] = new MadePlan(world.Mission, camp, chunks, reach, LandWithin(reach), plan);
        return plan;
    }

    /// <summary>
    /// Where the mission's plan expects the player's camp: the first building of tribe 0 among the
    /// mission's initial objects (the template of the enemy's camp for routes, traps and spells).
    /// </summary>
    public GridPoint TemplateEnemyCamp()
    {
        foreach (InitialObject item in world.Content.Rules.Missions.Missions[world.Mission].InitialObjects)
        {
            if (item.Kind == InitialObjectKind.Building && item.Tribe == TemplatePlayer)
            {
                return item.Cell;
            }
        }
        return Template.Home;
    }

    /// <summary>
    /// The cells whose land moving <paramref name="template"/> to <paramref name="camp"/> may read:
    /// its places and the corners of its alarm and territory areas moved (and kept inside the land,
    /// which holds the camp), the search for room round them with the widest planned building, and
    /// the edges of the land beside them.
    /// </summary>
    internal GridRect Reach(MissionAi template, GridPoint camp)
    {
        var shift = new CellOffset(camp.X - template.Home.X, camp.Y - template.Home.Y);
        int left = camp.X, top = camp.Y, right = camp.X, bottom = camp.Y, widest = 0;
        void Include(GridPoint cell)
        {
            var at = Shifted(cell, shift);
            (left, top, right, bottom) = (Math.Min(left, at.X), Math.Min(top, at.Y), Math.Max(right, at.X), Math.Max(bottom, at.Y));
        }
        Include(template.Home);
        foreach (AiBuildPlanEntry entry in template.BuildPlan)
        {
            Include(entry.Cell);
            BuildingKind kind = world.Content.Rules.Buildings.Kinds[entry.BuildingKind];
            widest = Math.Max(widest, Math.Max(kind.Width, kind.Height));
        }
        foreach (AiPost post in template.Posts)
        {
            Include(post.Cell);
        }
        Include(new GridPoint(template.AlarmRect.Left, template.AlarmRect.Top));
        Include(new GridPoint(template.AlarmRect.Right, template.AlarmRect.Bottom));
        Include(new GridPoint(template.TerritoryRect.Left, template.TerritoryRect.Top));
        Include(new GridPoint(template.TerritoryRect.Right, template.TerritoryRect.Bottom));
        int margin = RelocationRounds + widest + ClampReach + EdgeReach;
        return GridRect.FromEdges(left - margin, top - margin, right + 1 + margin, bottom + 1 + margin);
    }

    /// <summary>
    /// What a plan made within <paramref name="reach"/> depends on in the land: how many chunks are
    /// made there (land is only ever added, so the same count is the same land) and where the land's
    /// edges lie there.
    /// </summary>
    private LandKey LandWithin(GridRect reach)
    {
        GridRect bounds = world.Map.Bounds;
        GridRect edges = GridRect.FromEdges(Math.Max(bounds.Left, reach.Left), Math.Max(bounds.Top, reach.Top),
            Math.Min(bounds.Right, reach.Right), Math.Min(bounds.Bottom, reach.Bottom));
        int made = 0;
        if (world.Map.IsChunked)
        {
            ChunkCoord first = ChunkCoord.Of(new GridPoint(reach.Left, reach.Top));
            ChunkCoord last = ChunkCoord.Of(new GridPoint(reach.Right - 1, reach.Bottom - 1));
            for (int y = first.Y; y <= last.Y; y++)
            {
                for (int x = first.X; x <= last.X; x++)
                {
                    made += world.Map.Chunk(new ChunkCoord(x, y)) != null ? 1 : 0;
                }
            }
        }
        return new LandKey(made, edges);
    }

    private MissionAi Move(MissionAi template, GridPoint camp)
    {
        var shift = new CellOffset(camp.X - template.Home.X, camp.Y - template.Home.Y);
        var moved = new Dictionary<GridPoint, GridPoint>();
        var taken = new HashSet<GridPoint>();
        var buildPlan = new List<AiBuildPlanEntry>();
        foreach (AiBuildPlanEntry entry in template.BuildPlan)
        {
            GridPoint place = Fit(entry.BuildingKind, Clamp(Shifted(entry.Cell, shift)), taken);
            moved.TryAdd(entry.Cell, place);
            buildPlan.Add(entry with { Cell = place });
        }
        var posts = new List<AiPost>();
        foreach (AiPost post in template.Posts)
        {
            posts.Add(post with { Cell = moved.TryGetValue(post.Cell, out GridPoint at) ? at : Ground(Clamp(Shifted(post.Cell, shift))) });
        }
        return template with
        {
            Posts = posts,
            BuildPlan = buildPlan,
            Home = Ground(camp),
            AlarmRect = Shifted(template.AlarmRect, shift),
            TerritoryRect = Shifted(template.TerritoryRect, shift),
        };
    }

    /// <summary>The cell nearest <paramref name="wanted"/> where a building of <paramref name="kind"/> fits on ground, clear of the planned ones.</summary>
    private GridPoint Fit(int kind, GridPoint wanted, HashSet<GridPoint> taken)
    {
        BuildingKind definition = world.Content.Rules.Buildings.Kinds[kind];
        GridPoint found = wanted;
        bool fits = Fits(definition, wanted, taken);
        int dx = 0, dy = 0, sign = 1;
        for (int round = 1; round <= RelocationRounds && !fits; round++, sign = -sign)
        {
            for (int step = 0; step < 2 * round && !fits; step++)
            {
                if (step < round)
                {
                    dx += sign;
                }
                else
                {
                    dy += sign;
                }
                found = new GridPoint(wanted.X + dx, wanted.Y + dy);
                fits = Fits(definition, found, taken);
            }
        }
        if (!fits)
        {
            return wanted;
        }
        for (int y = 0; y < definition.Height; y++)
        {
            for (int x = 0; x < definition.Width; x++)
            {
                taken.Add(new GridPoint(found.X + x, found.Y + y));
            }
        }
        return found;
    }

    private bool Fits(BuildingKind definition, GridPoint origin, HashSet<GridPoint> taken)
    {
        for (int y = 0; y < definition.Height; y++)
        {
            for (int x = 0; x < definition.Width; x++)
            {
                var cell = new GridPoint(origin.X + x, origin.Y + y);
                if (!MatchTerrain.IsGround(world, cell) || taken.Contains(cell))
                {
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>The ground cell nearest <paramref name="cell"/> (the cell itself when there is none near).</summary>
    private GridPoint Ground(GridPoint cell) => MatchTerrain.NearestGround(world, cell, RelocationRounds) ?? cell;

    private GridPoint Clamp(GridPoint cell) => MatchTerrain.Inside(world, cell);

    private static GridPoint Shifted(GridPoint cell, CellOffset shift) => new(cell.X + shift.Dx, cell.Y + shift.Dy);

    /// <summary>The chunks made within a plan's reach and the land's edges there.</summary>
    private readonly record struct LandKey(int Chunks, GridRect Edges);

    /// <summary>A plan made for a camp, with the map's chunk count and the land it read when last checked.</summary>
    private sealed record MadePlan(int Mission, GridPoint Camp, int Chunks, GridRect Reach, LandKey Land, MissionAi Plan);

    private CellRect Shifted(CellRect rect, CellOffset shift)
    {
        GridPoint topLeft = Clamp(new GridPoint(rect.Left + shift.Dx, rect.Top + shift.Dy));
        GridPoint bottomRight = Clamp(new GridPoint(rect.Right + shift.Dx, rect.Bottom + shift.Dy));
        return new CellRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }
}
