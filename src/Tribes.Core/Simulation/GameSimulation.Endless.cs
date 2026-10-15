using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Endless;
using Tribes.Core.World;

namespace Tribes.Core.Simulation;

/// <summary>Endless worlds: starting one, and making land on demand for tools and tests.</summary>
public sealed partial class GameSimulation
{
    /// <summary>
    /// A new endless world of <paramref name="setup"/>. Hints do not pause it unless
    /// <paramref name="options"/> says so (a world played alone does).
    /// </summary>
    /// <exception cref="ArgumentException">The setup cannot start (see <see cref="EndlessProblem"/>).</exception>
    public static GameSimulation StartEndless(GameContent content, EndlessSetup setup, SimulationOptions? options = null) =>
        new(EndlessStart.Begin(content, setup), 0, options ?? new SimulationOptions { HintsPauseGame = false });

    /// <summary>Why <paramref name="setup"/> cannot start an endless world, or null when it can.</summary>
    public static string? EndlessProblem(GameContent content, EndlessSetup setup) => EndlessStart.Problem(content, setup);

    /// <summary>Whether this is an endless world (its map is made of chunks, <see cref="Tribes.Core.World.GameWorld.Endless"/> is set).</summary>
    public bool IsEndless => World.Endless != null;

    /// <summary>
    /// Makes every chunk touching <paramref name="area"/> that is not made yet (for tools, tests and
    /// a host preparing a big world): part of the game's state, so every peer must do the same
    /// between the same ticks. No new tribe comes into this land. Returns the chunks made.
    /// </summary>
    /// <exception cref="InvalidOperationException">The world is not endless.</exception>
    public IReadOnlyList<ChunkCoord> GenerateLand(GridRect area)
    {
        if (!IsEndless)
        {
            throw new InvalidOperationException("only endless worlds make land");
        }
        List<ChunkCoord> made = systems.Land.EnsureArea(area);
        systems.Land.Announce();
        return made;
    }

    /// <summary>
    /// The test button of a single-player game: <paramref name="tribe"/> explores at once the land
    /// within <paramref name="radius"/> cells of its camp, and remembers the buildings there. An
    /// endless world makes that land first, and new tribes may come into it as if the tribe had
    /// walked there. Changes the game's state outside the orders, so never in a game with others.
    /// </summary>
    public void ExploreForTest(int tribe, int radius)
    {
        GameWorld world = World;
        if (tribe < 0 || tribe >= world.Tribes.Count || radius <= 0)
        {
            return;
        }
        GridPoint camp = world.Tribes[tribe].Camp;
        GridRect area = GridRect.FromEdges(camp.X - radius, camp.Y - radius, camp.X + radius + 1, camp.Y + radius + 1);
        if (IsEndless)
        {
            List<ChunkCoord> made = systems.Land.EnsureArea(area);
            systems.TribeSpawner.Consider(made);
            systems.Land.Announce();
        }
        else
        {
            GridRect bounds = world.Map.Bounds;
            area = GridRect.FromEdges(Math.Max(area.Left, bounds.Left), Math.Max(area.Top, bounds.Top),
                Math.Min(area.Right, bounds.Right), Math.Min(area.Bottom, bounds.Bottom));
        }
        systems.Sight.ExploreForTest(tribe, area);
    }
}
