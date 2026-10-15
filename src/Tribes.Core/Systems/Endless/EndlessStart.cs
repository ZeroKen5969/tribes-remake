using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>
/// The choices an endless world starts with. Tribe numbers are the positions in
/// <see cref="Tribes"/> (the people, in slot order, and any computer tribes listed), then the
/// <see cref="ComputerTribes"/> more computer tribes; every tribe gets a camp.
/// </summary>
public sealed record EndlessSetup
{
    /// <summary>The world seed: the land, the habitats, the new tribes and the rules' random draws come from it.</summary>
    public long Seed { get; init; } = 1;

    /// <summary>The difficulty of computer tribes whose controller names none (0 easy, 1 hard).</summary>
    public int Difficulty { get; init; }

    /// <summary>The tribes listed, in tribe order (at least one). Endless worlds have no teams.</summary>
    public IReadOnlyList<MatchTribe> Tribes { get; init; } = Array.Empty<MatchTribe>();

    /// <summary>
    /// Computer tribes added after the listed ones (any number): the first ones (up to the rules'
    /// <c>placedAtStart</c>) get camps at the start, the others come into land the people uncover
    /// (<see cref="EndlessState.WaitingTribes"/>).
    /// </summary>
    public int ComputerTribes { get; init; }

    /// <summary>How often new computer tribes appear in land the people uncover: 0 none, 1 few, 2 steady, 3 many.</summary>
    public int NewTribes { get; init; } = 2;

    /// <summary>Side in cells of the land made around the start; 0 for the rules' default.</summary>
    public int StartAreaSize { get; init; }

    /// <summary>Whether the players start with hints switched on.</summary>
    public bool HintsEnabled { get; init; } = true;

    /// <summary>
    /// The lobby's settings (opening grace, start stock, wild animals); null for the endless rules'
    /// defaults (their opening grace, the map's stock, wild animals).
    /// </summary>
    public WorldSettings? Settings { get; init; }

    /// <summary>
    /// For tests and developers' scripted runs: every people's tribe starts with a finished alliance
    /// building and <see cref="EndlessStart.DevStock"/> of wood and stones (part of the setup, so all
    /// peers start alike).
    /// </summary>
    public bool DevAllianceHalls { get; init; }
}

/// <summary>
/// Starting an endless world: land made around (0, 0) by the generator, the custom mission of the
/// endless rules for everything else (units, buildings, computer plans, animals' kinds), a camp for
/// every tribe found as far out as it takes, and a world that never ends (<see cref="MatchState.EndsWithLastSide"/> off).
/// </summary>
public static class EndlessStart
{
    /// <summary>The spawn gender mode a game starts with (keep the balance of men and women).</summary>
    private const int BalancedGenders = 2;

    /// <summary>The highest density setting.</summary>
    public const int MostNewTribes = 3;

    /// <summary>Wood and stones a people's tribe gets with <see cref="EndlessSetup.DevAllianceHalls"/>.</summary>
    public const int DevStock = 300;

    /// <summary>How far from its camp (cells) the alliance building of <see cref="EndlessSetup.DevAllianceHalls"/> may stand.</summary>
    private const int DevHallReach = 16;

    /// <summary>Why <paramref name="setup"/> cannot start an endless world, or null when it can.</summary>
    public static string? Problem(GameContent content, EndlessSetup setup)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(setup);
        if (content.Endless == null)
        {
            return "the content has no endless rules";
        }
        if (setup.Tribes.Count == 0)
        {
            return "an endless world needs a tribe";
        }
        if (setup.ComputerTribes < 0 || setup.StartAreaSize < 0)
        {
            return "tribe count and start area cannot be negative";
        }
        if (setup.NewTribes is < 0 or > MostNewTribes)
        {
            return $"new tribes are 0..{MostNewTribes}";
        }
        if (setup.Settings?.Problem() is string settings)
        {
            return settings;
        }
        return setup.Difficulty is 0 or 1 ? null : "difficulty is 0 or 1";
    }

    /// <summary>A new endless world playing <paramref name="setup"/>, with its rule systems.</summary>
    /// <exception cref="ArgumentException">The setup cannot start (see <see cref="Problem"/>).</exception>
    public static RuleSystems Begin(GameContent content, EndlessSetup setup)
    {
        if (Problem(content, setup) is string problem)
        {
            throw new ArgumentException(problem, nameof(setup));
        }
        GameContent endless = EndlessContent.For(content);
        EndlessRules rules = endless.Endless!;
        RuleSystems systems = NewWorld(endless, setup);
        GameWorld world = systems.World;
        CampIndex.Starting(world);
        int side = setup.StartAreaSize > 0 ? setup.StartAreaSize : rules.Land.StartAreaCells;
        systems.Land.EnsureArea(new GridRect(-(side / 2), -(side / 2), side, side));
        var keepAway = new CampDistances(rules.Camps.MinimumDistance);
        systems.EndlessCampFinder.Starting(true);
        for (int t = 0; t < setup.Tribes.Count; t++)
        {
            GridPoint camp = systems.EndlessCampFinder.Find(world, keepAway);
            keepAway.Add(camp);
            MatchStart.Settle(systems, t, camp);
            systems.Raise(new TribeAppeared(t, camp, world.Tribes[t].Controller.RunsAi));
            if (setup.DevAllianceHalls && world.Tribes[t].Controller.IsHumanRuled)
            {
                GiveAllianceHall(systems, t);
            }
        }
        int placed = Math.Min(setup.ComputerTribes, rules.Camps.PlacedAtStart);
        for (int i = 0; i < placed; i++)
        {
            int tribe = MatchStart.AddTribe(systems, TribeController.Computer(), TribeState.NoTeam, systems.EndlessCampFinder);
            systems.Raise(new TribeAppeared(tribe, world.Tribes[tribe].Camp, Computer: true));
        }
        systems.EndlessCampFinder.Starting(false);
        world.Endless!.WaitingTribes = setup.ComputerTribes - placed;
        systems.Sight.UpdateSight();
        MakeLandAhead(systems);
        return systems;
    }

    /// <summary>Makes the land within reach of every tribe at once, so the first frames have nothing to make.</summary>
    internal static void MakeLandAhead(RuleSystems systems)
    {
        foreach (ChunkCoord chunk in systems.Land.Missing(new HashSet<ChunkCoord>()))
        {
            systems.Land.Make(chunk);
        }
        systems.Land.Announce();
    }

    /// <summary>A finished alliance building next to the camp of <paramref name="tribe"/>, and wood and stones.</summary>
    private static void GiveAllianceHall(RuleSystems systems, int tribe)
    {
        GameWorld world = systems.World;
        world.Tribes[tribe][TribeCounter.Wood] += DevStock;
        world.Tribes[tribe][TribeCounter.Stones] += DevStock;
        GridPoint camp = world.Tribes[tribe].Camp;
        for (int ring = 1; ring <= DevHallReach; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    var cell = new GridPoint(camp.X + dx, camp.Y + dy);
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == ring
                        && systems.BuildingPlacement.Place(new Entities.PlacementRequest(tribe, AllianceBuilding.Kind, cell, 1, Immediate: true))
                            .Outcome == Entities.PlacementOutcome.Placed)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static RuleSystems NewWorld(GameContent content, EndlessSetup setup)
    {
        var world = new GameWorld(content, WorldMap.Chunked(), WorldLayout.ForMatch(setup.Tribes.Count), new GameRandom(setup.Seed))
        {
            Difficulty = setup.Difficulty,
            SpawnGenderMode = BalancedGenders,
        };
        world.Mode = WorldMode.Match;
        world.Match.EndsWithLastSide = false;
        world.Settings = setup.Settings ?? new WorldSettings { OpeningGraceFrames = content.Endless!.Pacing.OpeningGraceFrames };
        world.Endless = new EndlessState(setup.Seed, setup.NewTribes);
        for (int t = 0; t < setup.Tribes.Count; t++)
        {
            world.SetController(t, setup.Tribes[t].Controller);
            world.Tribes[t].Hints.Enabled = setup.HintsEnabled;
        }
        var systems = new RuleSystems(world);
        world.Mission = content.Endless!.Mission;
        systems.MissionReset.Reset();
        return systems;
    }
}
