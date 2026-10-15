using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Tests.MultiTribe;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Endless;

/// <summary>No order stops an endless world either (alliance orders and the alliance building included).</summary>
public sealed class EndlessOrderFuzzTests
{
    private readonly ITestOutputHelper output;

    public EndlessOrderFuzzTests(ITestOutputHelper output) => this.output = output;

    private static (List<ulong> Hashes, int[] Outcomes) Run(int seed, int ticks)
    {
        GameSimulation game = GameSimulation.StartEndless(EndlessWorlds.Content,
            EndlessWorlds.Setup(humans: 3, computers: 2, seed: seed, newTribes: 3) with { DevAllianceHalls = true });
        var fuzzer = new OrderFuzzer(seed);
        var hashes = new List<ulong>();
        int[] outcomes = new int[System.Enum.GetValues<OrderOutcome>().Length];
        for (int tick = 1; tick <= ticks; tick++)
        {
            foreach (OrderOutcome outcome in game.Step(fuzzer.Tick(game)).Outcomes)
            {
                outcomes[(int)outcome]++;
            }
            if (tick % 500 == 0)
            {
                hashes.Add(game.StateHash());
            }
        }
        return (hashes, outcomes);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void RandomOrders_InAnEndlessWorld_NeverThrow_AndAreDeterministic(int seed)
    {
        (List<ulong> hashes, int[] outcomes) = Run(seed, 3000);
        (List<ulong> again, _) = Run(seed, 3000);

        Assert.Equal(hashes, again);
        output.WriteLine(string.Join(", ", outcomes.Select((n, i) => $"{(OrderOutcome)i}={n}")));
        Assert.True(outcomes[(int)OrderOutcome.Applied] > 100);
    }

    [Fact]
    public void AllianceOrders_FromTheFuzzer_ReachTheRules()
    {
        GameSimulation game = GameSimulation.StartEndless(EndlessWorlds.Content,
            EndlessWorlds.Setup(humans: 2, computers: 0, seed: 3) with { DevAllianceHalls = true });

        TickResult invited = game.Step(TickInput.WithOrders(new InviteAlly(0, 1), new AnswerAlliance(1, 0, true),
            new TransferGoods(0, 1, World.TribeCounter.Stones, 5), new LeaveAlliance(1, 0)));

        Assert.All(invited.Outcomes, o => Assert.Equal(OrderOutcome.Applied, o));
        Assert.Contains(invited.Events, e => e is GoodsTransferred);
    }
}
