using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Godot;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Client.World.Dev;

/// <summary>
/// The dev scene running a game of its own (frame-rate and memory checks with many tribes):
/// <list type="bullet">
/// <item><c>--match=MAP</c>: a match on custom map MAP (0..) with <c>--humans=H</c> human tribes
/// (they get no orders) and <c>--computers=C</c> computer tribes, seed <c>--seed=S</c>.</item>
/// <item><c>--endless=SEED</c>: an endless world with the same tribes (start area <c>--area=N</c>
/// cells); <c>--explore=N</c> makes the land of an N x N square around the start and lets the
/// viewer have explored it (a large explored world without playing for hours).</item>
/// <item><c>--viewer=T</c>: the tribe the view belongs to (default 0); <c>--reveal</c>: it has explored everything;
/// <c>--spectate</c>: the view is a spectator's (every human tribe's sight).</item>
/// <item><c>--teams=A,B,..</c>: the teams of the match's tribes in order (humans first, -1 none);
/// <c>--allies=A-B,C-D</c>: pairs of tribes allied at the start (shared vision shows from the first sight update).</item>
/// <item><c>--run</c>: the rules run at their pace (otherwise the start is shown still).</item>
/// </list>
/// </summary>
public partial class WorldTest
{
    private GameSimulation? simulation;
    private double simulationClock;
    private double simulationMilliseconds, simulationPeak;

    // Every rule step's time since the start (for the WORLD_SIM line).
    private readonly List<double> stepTimes = new();

    /// <summary>Starts the game the arguments ask for; false when they ask for none.</summary>
    private bool StartSimulation(Dictionary<string, string> args, float zoom)
    {
        bool endless = args.TryGetValue("endless", out string? endlessSeed);
        if (!args.TryGetValue("match", out string? map) && !endless)
        {
            return false;
        }
        int humans = Number(args, "humans", 1), computers = Number(args, "computers", 1);
        var tribes = new List<MatchTribe>();
        for (int h = 0; h < humans; h++)
        {
            tribes.Add(new MatchTribe(TribeController.Human(h)));
        }
        if (endless)
        {
            StartEndless(args, long.Parse(endlessSeed!, CultureInfo.InvariantCulture), tribes, computers, zoom);
            return true;
        }
        for (int c = 0; c < computers; c++)
        {
            tribes.Add(new MatchTribe(TribeController.Computer()));
        }
        if (args.TryGetValue("teams", out string? teams))
        {
            string[] team = teams.Split(',');
            for (int t = 0; t < tribes.Count && t < team.Length; t++)
            {
                tribes[t] = tribes[t] with { Team = int.Parse(team[t], CultureInfo.InvariantCulture) };
            }
        }
        simulation = GameSimulation.StartMatch(content, new MatchSetup
        {
            Map = int.Parse(map!, CultureInfo.InvariantCulture),
            Seed = Number(args, "seed", 1),
            Tribes = tribes,
            HintsEnabled = false,
        });
        ShowSimulation(args, zoom);
        GD.Print($"WORLD_TEST match map {map}: {humans} humans, {computers} computer tribes, {simulation.World.Tribes.Count} tribes in all");
        return true;
    }

    private void StartEndless(Dictionary<string, string> args, long seed, List<MatchTribe> humans, int computers, float zoom)
    {
        var watch = Stopwatch.StartNew();
        simulation = GameSimulation.StartEndless(content, new EndlessSetup
        {
            Seed = seed,
            Tribes = humans,
            ComputerTribes = computers,
            StartAreaSize = Number(args, "area", 128),
            HintsEnabled = false,
        });
        double started = watch.Elapsed.TotalMilliseconds;
        int explore = Number(args, "explore", 0);
        if (explore > 0)
        {
            GameWorld world = simulation.World;
            GridPoint camp = world.Tribes[Math.Clamp(Number(args, "viewer", 0), 0, world.Tribes.Count - 1)].Camp;
            var area = new GridRect(camp.X - (explore / 2), camp.Y - (explore / 2), explore, explore);
            simulation.GenerateLand(area);
            Reveal(world.Fog(Math.Clamp(Number(args, "viewer", 0), 0, world.Tribes.Count - 1)), Scene.MapArea.Of(area));
        }
        ShowSimulation(args, zoom);
        Scene.CellRect bounds = Scene.MapArea.Bounds(simulation.World.Map);
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"WORLD_TEST endless seed {seed}: {simulation.World.Tribes.Count} tribes, {simulation.World.Map.Chunks.Count} chunks, bounds {bounds}, viewer camp {simulation.World.Tribes[view.Looks.Viewer].Camp}, started in {started:0} ms, land made in {watch.Elapsed.TotalMilliseconds - started:0} ms"));
    }

    private void ShowSimulation(Dictionary<string, string> args, float zoom)
    {
        GameWorld world = simulation!.World;
        int viewer = Math.Clamp(Number(args, "viewer", 0), 0, world.Tribes.Count - 1);
        if (args.ContainsKey("reveal") && !Scene.MapArea.IsChunked(world.Map))
        {
            Reveal(world.Fog(viewer), Scene.MapArea.Bounds(world.Map));
        }
        Ally(world, args);
        LineUp(world, args);
        MemoryDemo(world, args);
        view.Show(world, args.ContainsKey("spectate") ? Scene.Viewpoint.Spectator(viewer) : Scene.Viewpoint.Player(viewer));
        camera.Bounds = view.MapBounds;
        camera.Zoom = new Vector2(zoom, zoom);
        GridPoint camp = lineupCentre ?? LookAt(world, args, viewer);
        camera.CentreOn(new Vector2((camp.X + 0.5f) * WorldView.CellWidth, (camp.Y + 0.5f) * WorldView.CellHeight));
        runSimulation = args.ContainsKey("run");
    }

    private bool runSimulation;

    /// <summary>Makes the pairs <c>--allies=A-B,..</c> allies and works out their sight at once.</summary>
    private void Ally(GameWorld world, Dictionary<string, string> args)
    {
        if (!args.TryGetValue("allies", out string? pairs))
        {
            return;
        }
        foreach (string pair in pairs.Split(','))
        {
            string[] ends = pair.Split('-');
            world.Relations.SetAlliance(int.Parse(ends[0], CultureInfo.InvariantCulture), int.Parse(ends[1], CultureInfo.InvariantCulture), true);
        }
        for (int i = 0; i < SightFrames; i++)
        {
            simulation!.Step();
        }
    }

    /// <summary>Rule frames between two sight updates.</summary>
    private const int SightFrames = 4;

    /// <summary>Marks every cell of <paramref name="cells"/> explored and seen.</summary>
    private static void Reveal(FogOfWar fog, Scene.CellRect cells)
    {
        for (int y = cells.Top; y < cells.Bottom; y++)
        {
            for (int x = cells.Left; x < cells.Right; x++)
            {
                fog.SetExplored(new GridPoint(x, y), true);
                fog.SetVisible(new GridPoint(x, y), true);
            }
        }
    }

    /// <summary>Runs the rules at their pace (<c>--run</c>), one rule frame per <see cref="WorldView.RuleFrameSeconds"/>.</summary>
    private void StepSimulation(double delta)
    {
        if (simulation == null || !runSimulation)
        {
            return;
        }
        simulationClock += delta;
        if (simulationClock < view.RuleFrameSeconds)
        {
            return;
        }
        simulationClock = Math.Min(simulationClock - view.RuleFrameSeconds, view.RuleFrameSeconds);
        long start = Stopwatch.GetTimestamp();
        TickResult result = simulation.Step();
        simulationMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        simulationPeak = Math.Max(simulationPeak, simulationMilliseconds);
        stepTimes.Add(simulationMilliseconds);
        view.OnRuleFrame(result.Events);
    }

    /// <summary>
    /// The rule steps so far, leaving out the first 25 (start-up): count, median, 99th percentile and
    /// slowest, in milliseconds; null before there are any.
    /// </summary>
    private string? SimulationSummary()
    {
        const int Skipped = 25;
        if (stepTimes.Count <= Skipped)
        {
            return null;
        }
        var sorted = stepTimes.GetRange(Skipped, stepTimes.Count - Skipped);
        sorted.Sort();
        double At(double share) => sorted[Math.Min(sorted.Count - 1, (int)(share * sorted.Count))];
        return string.Create(CultureInfo.InvariantCulture,
            $"WORLD_SIM steps {sorted.Count} median {At(0.5):0.00}ms p99 {At(0.99):0.00}ms max {sorted[^1]:0.00}ms");
    }

    private static int Number(Dictionary<string, string> args, string name, int fallback) =>
        args.TryGetValue(name, out string? text) ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;
}
