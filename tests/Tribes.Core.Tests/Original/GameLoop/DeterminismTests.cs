using System.Collections.Generic;
using System.IO;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Tests.Systems;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.GameLoop;

/// <summary>
/// Determinism and saved games: two games given the same input have the same state hash on every
/// tick, and a game saved at any tick and loaded goes on exactly as if it had never stopped.
/// </summary>
[Trait("Speed", "Long")]
public sealed class DeterminismTests
{
    /// <summary>Ticks each mission is played.</summary>
    private const int Ticks = 3000;

    /// <summary>Ticks at which games are saved (one of them while the opening scene plays).</summary>
    private static readonly int[] SaveTicks = { 40, 700, 1650, 2900 };

    private readonly ITestOutputHelper output;

    public DeterminismTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SameInput_GivesTheSameStateOnEveryTick()
    {
        for (int mission = 0; mission < LegacySamples.Content.Missions.Count; mission++)
        {
            ulong[] first = Play(Start(mission), 0, Ticks, mission);
            ulong[] second = Play(Start(mission), 0, Ticks, mission);
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void SavedAndLoaded_GoesOnAsIfUninterrupted()
    {
        int checkedTicks = 0;
        for (int mission = 0; mission < LegacySamples.Content.Missions.Count; mission++)
        {
            GameSimulation game = Start(mission);
            var saves = new Dictionary<int, byte[]>();
            var hashes = new ulong[Ticks];
            for (int tick = 0; tick < Ticks; tick++)
            {
                if (System.Array.IndexOf(SaveTicks, tick) >= 0)
                {
                    saves[tick] = game.Save();
                }
                game.Step(ScriptedPlayer.InputFor(game, game.Tick, mission));
                hashes[tick] = game.StateHash();
            }
            foreach ((int tick, byte[] save) in saves)
            {
                GameSimulation loaded = GameSimulation.Load(LegacySamples.Content, save);
                Assert.Equal(tick, loaded.Tick);
                Assert.Equal(save, loaded.Save());
                ulong[] resumed = Play(loaded, tick, Ticks, mission);
                for (int t = tick; t < Ticks; t++)
                {
                    Assert.True(hashes[t] == resumed[t], $"mission {mission}: saved at {tick}, tick {t} differs");
                }
                checkedTicks += Ticks - tick;
            }
            output.WriteLine($"mission {mission}: world frame {game.World.Frame}, over {game.IsOver}");
        }
        output.WriteLine($"{checkedTicks} resumed ticks compared");
    }

    [Fact]
    public void Load_RefusesOtherData()
    {
        Assert.Throws<InvalidDataException>(() => GameSimulation.Load(LegacySamples.Content, new byte[16]));
        GameSimulation game = Start(2);
        byte[] save = game.Save();
        save[4] = 99;
        Assert.Throws<InvalidDataException>(() => GameSimulation.Load(LegacySamples.Content, save));
    }

    private static GameSimulation Start(int mission) =>
        GameSimulation.StartMission(LegacySamples.Content, mission, mission % 2, new MissionOptions { Seed = 17 + mission });

    /// <summary>Plays <paramref name="game"/> from tick <paramref name="from"/> to <paramref name="to"/>; the hash after each tick by tick.</summary>
    private static ulong[] Play(GameSimulation game, int from, int to, int seed)
    {
        var hashes = new ulong[to];
        for (int tick = from; tick < to; tick++)
        {
            game.Step(ScriptedPlayer.InputFor(game, game.Tick, seed));
            hashes[tick] = game.StateHash();
        }
        return hashes;
    }
}
