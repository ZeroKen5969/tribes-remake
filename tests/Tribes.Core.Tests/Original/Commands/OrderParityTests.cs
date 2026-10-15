using System.IO;
using System.Linq;
using Tribes.Core.Tests.Systems;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Commands;

/// <summary>
/// The players' orders against the original's input handlers in key-monkey games (seeds 1, 4, 6):
/// every order a handler carries out is applied by the new rules to the state just before the
/// handler, and the rule states must agree afterwards; no frame's input may change the rule state
/// without an order.
/// </summary>
public sealed class OrderParityTests
{
    private const int Frames = 30000;

    private readonly ITestOutputHelper output;

    public OrderParityTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(4L)]
    [InlineData(6L)]
    public void MonkeyInput_ChangesTheWorldAsTheOriginalHandlersDo(long seed)
    {
        string resources = Path.Combine(TestPaths.RepoRoot(), "original", "game", "res");
        InputRun run = InputComparison.Run(resources, LegacySamples.Content, seed, Frames,
            (game, report, world) => OriginalOrders.Rewrite(LegacySamples.Content, report, world));
        output.WriteLine(run.ToString());
        Assert.True(run.FailureCount == 0, run.ToString());
        Assert.True(run.UnexplainedCount == 0, run.ToString());
        Assert.True(run.Compared.Values.Sum() > 0, "the monkey gave no orders");
    }
}
