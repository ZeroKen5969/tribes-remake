using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.Tests.MultiTribe;
using Tribes.Core.World;

namespace Tribes.Core.Tests.Endless;

/// <summary>Endless worlds for the tests.</summary>
internal static class EndlessWorlds
{
    /// <summary>The game's content (with the endless rules).</summary>
    public static GameContent Content => MatchWorlds.Content;

    /// <summary>A setup of <paramref name="humans"/> people and <paramref name="computers"/> computer tribes.</summary>
    public static EndlessSetup Setup(int humans, int computers, long seed = 5, int newTribes = 0, int difficulty = 0) => new()
    {
        Seed = seed,
        Difficulty = difficulty,
        Tribes = Enumerable.Range(0, humans).Select(h => new MatchTribe(TribeController.Human(h))).ToArray(),
        ComputerTribes = computers,
        NewTribes = newTribes,
    };

    /// <summary>A new endless world of the setup.</summary>
    public static GameSimulation Start(int humans, int computers, long seed = 5, int newTribes = 0) =>
        GameSimulation.StartEndless(Content, Setup(humans, computers, seed, newTribes));
}
