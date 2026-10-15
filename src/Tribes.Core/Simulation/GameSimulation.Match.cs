using System;
using Tribes.Core.Content;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;

namespace Tribes.Core.Simulation;

/// <summary>Matches of several tribes: starting one, its players coming and going, tribes joining, its result.</summary>
public sealed partial class GameSimulation
{
    /// <summary>
    /// A new match of <paramref name="setup"/> (tribe t is <c>setup.Tribes[t]</c>). Hints do not
    /// pause a match unless <paramref name="options"/> says so.
    /// </summary>
    /// <exception cref="ArgumentException">The setup cannot start a match (see <see cref="MatchProblem"/>).</exception>
    public static GameSimulation StartMatch(GameContent content, MatchSetup setup, SimulationOptions? options = null) =>
        new(MatchStart.Begin(content, setup), 0, options ?? new SimulationOptions { HintsPauseGame = false });

    /// <summary>Whether this game is a match (versus, endless) rather than the campaign.</summary>
    public bool IsMatch => World.Mode == WorldMode.Match;

    /// <summary>The standing of the match (every tribe in the campaign counts as playing).</summary>
    public MatchResult Match => MatchStandings.Of(World);

    /// <summary>Whether <paramref name="tribe"/> is out of the match: wiped out or given up.</summary>
    public bool IsDefeated(int tribe) => MatchStandings.IsDefeated(World, tribe);

    /// <summary>
    /// Changes who plays <paramref name="tribe"/>, between ticks (it is part of the game's state and
    /// is saved): a human player leaves (<see cref="TribeController.Absent"/>), comes back
    /// (<see cref="TribeController.Human"/>) or gives up (<see cref="TribeController.Forfeited"/>,
    /// final: the tribe goes out and stays as an idle tribe), or a computer's difficulty changes.
    /// No human-ruled tribe is ever given to the computer.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no such tribe.</exception>
    /// <exception cref="InvalidOperationException">The change is not allowed (see above).</exception>
    public void SetController(int tribe, TribeController controller)
    {
        if (tribe < 0 || tribe >= World.Tribes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(tribe), tribe, $"tribes are 0..{World.Tribes.Count - 1}");
        }
        TribeController current = World.Tribes[tribe].Controller;
        if (current.Kind == TribeControl.Forfeited && controller != current)
        {
            throw new InvalidOperationException("a tribe given up stays given up");
        }
        if (current.IsHumanRuled != controller.IsHumanRuled)
        {
            throw new InvalidOperationException("a human-ruled tribe stays human-ruled and a computer tribe stays the computer's");
        }
        World.SetController(tribe, controller);
        if (controller.Kind == TribeControl.Forfeited && IsMatch)
        {
            systems.MatchUpdate.GoOut(World.Tribes[tribe]);
            systems.MatchUpdate.Decide();
        }
    }

    /// <summary>
    /// Adds a tribe to a match between ticks (a player joining, a new computer tribe): played by
    /// <paramref name="controller"/> on <paramref name="team"/>, with a new camp on open ground far
    /// from the others. Returns its number (the next one), or -1 when the map has no room for a camp;
    /// in an endless world it always gets a camp (land is made until one fits).
    /// </summary>
    /// <exception cref="InvalidOperationException">The game is not a match.</exception>
    public int AddTribe(TribeController controller, int team = TribeState.NoTeam) =>
        AddTribe(controller, team, IsEndless ? systems.EndlessCampFinder : MapCampFinder.Instance);

    /// <summary>
    /// Adds a tribe as <see cref="AddTribe(TribeController, int)"/> does, its camp where
    /// <paramref name="finder"/> finds room (a growing world makes more land first).
    /// </summary>
    /// <exception cref="InvalidOperationException">The game is not a match.</exception>
    public int AddTribe(TribeController controller, int team, ICampFinder finder)
    {
        ArgumentNullException.ThrowIfNull(finder);
        if (!IsMatch)
        {
            throw new InvalidOperationException("tribes join matches only");
        }
        int tribe = MatchStart.AddTribe(systems, controller, team, finder);
        if (tribe >= 0 && IsEndless)
        {
            systems.Raise(new Systems.Endless.TribeAppeared(tribe, World.Tribes[tribe].Camp, controller.RunsAi));
        }
        return tribe;
    }

    /// <summary>
    /// Why <paramref name="setup"/> cannot start a match (no tribe, no such map, more tribes than
    /// <see cref="MatchStart.CampCapacity"/>), or null when it can; <see cref="StartMatch"/> refuses
    /// such a setup with an <see cref="ArgumentException"/>.
    /// </summary>
    public static string? MatchProblem(GameContent content, MatchSetup setup) => MatchStart.Problem(content, setup);

    /// <summary>
    /// Lifts the original's limits between ticks: from now on every tribe has as many units and
    /// buildings as it needs and the world's tables grow, as in a match (a single-player custom game
    /// started with <see cref="Systems.Missions.MissionOptions.WithoutLimits"/> has it from the start; this is for one
    /// saved before the limits were lifted). A match has no limits anyway.
    /// </summary>
    public void LiftLimits() => World.LiftLimits();
}
