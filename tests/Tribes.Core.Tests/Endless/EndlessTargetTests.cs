using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Ai;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.Endless;

/// <summary>Who a computer tribe goes for in an endless world: the people within reach come first.</summary>
public sealed class EndlessTargetTests
{
    private const int Humans = 2, Computers = 8;

    private static EndlessRules Rules => EndlessWorlds.Content.Endless!;

    private static long Reach => (long)Rules.Pacing.HumanReachCells * Rules.Pacing.HumanReachCells;

    [Fact]
    public void AComputerWithPeopleInReach_GoesForOneOfThem()
    {
        int checkedComputers = 0;
        foreach (long seed in new long[] { 1, 2, 3 })
        {
            GameSimulation game = AfterTheGrace(seed);
            for (int computer = Humans; computer < Humans + Computers; computer++)
            {
                if (PeopleInReach(game, computer).Length > 0)
                {
                    Assert.True(TribeRoles.IsPlayer(game.World, game.Rules.ComputerTargets.Choose(computer)), $"seed {seed}, computer {computer}");
                    checkedComputers++;
                }
            }
        }
        Assert.True(checkedComputers >= 3, $"only {checkedComputers} computers had people in reach");
    }

    [Fact]
    public void OfSeveralPeopleInReach_TheOneFewerComputersAttack_ThenTheNearer()
    {
        foreach (long seed in Enumerable.Range(1, 10).Select(s => (long)s))
        {
            GameSimulation game = AfterTheGrace(seed);
            GameWorld world = game.World;
            ComputerTargets targets = game.Rules.ComputerTargets;
            for (int computer = Humans; computer < Humans + Computers; computer++)
            {
                int[] people = PeopleInReach(game, computer);
                if (people.Length < 2)
                {
                    continue;
                }
                int nearer = people.OrderBy(p => Distance(game, computer, p)).ThenBy(p => p).First();
                int other = people.First(p => p != nearer);
                Assert.Equal(nearer, targets.Choose(computer));

                int helper = Enumerable.Range(Humans, Computers).First(c => c != computer);
                world.Tribes[helper].Ai.WaveTarget = 0;
                world.Tribes[helper].Ai.WaveTribe = nearer;

                Assert.Equal(other, targets.Choose(computer));
                return;
            }
        }
        Assert.Fail("no computer had two people in reach in seeds 1..10");
    }

    [Fact]
    public void PeopleBeyondTheReach_AreChosenAsBefore_AmongTheNearEnemies()
    {
        GameSimulation game = AfterTheGrace(1);
        for (int computer = Humans; computer < Humans + Computers; computer++)
        {
            if (PeopleInReach(game, computer).Length > 0)
            {
                continue;
            }
            int choice = game.Rules.ComputerTargets.Choose(computer);
            long[] distances = TribeRoles.EnemiesOf(game.World, computer)
                .Where(e => EndlessPacing.MayTarget(game.World, computer, e, Distance(game, computer, e)))
                .Select(e => Distance(game, computer, e)).ToArray();
            Assert.True(Distance(game, computer, choice) * 4 <= distances.Min() * 9, $"computer {computer} went beyond the near enemies");
            return;
        }
        Assert.Fail("every computer had people in reach");
    }

    private static GameSimulation AfterTheGrace(long seed)
    {
        GameSimulation game = EndlessWorlds.Start(humans: Humans, computers: Computers, seed: seed);
        game.World.Frame = Rules.Pacing.OpeningGraceFrames;
        return game;
    }

    /// <summary>The human-ruled enemies <paramref name="computer"/> may go for within the people's reach.</summary>
    private static int[] PeopleInReach(GameSimulation game, int computer) =>
        Enumerable.Range(0, Humans)
            .Where(h => Distance(game, computer, h) <= Reach && EndlessPacing.MayTarget(game.World, computer, h, Distance(game, computer, h)))
            .ToArray();

    private static long Distance(GameSimulation game, int computer, int tribe)
    {
        GridPoint home = game.Rules.ComputerPlans.For(computer).Home, seat = TribeStanding.Seat(game.World, tribe);
        return ((long)(home.X - seat.X) * (home.X - seat.X)) + ((long)(home.Y - seat.Y) * (home.Y - seat.Y));
    }
}
