using Tribes.Core;
using Xunit;
using LegacyRandom = Tribes.Legacy.Runtime.Random;

namespace Tribes.Core.Tests;

public sealed class GameRandomTests
{
    [Fact]
    public void NextInt_FollowsJavaSequence()
    {
        // java.util.Random(1L).nextInt() on JDK 17.
        var random = new GameRandom(1L);

        Assert.Equal(-1155869325, random.NextInt());
        Assert.Equal(431529176, random.NextInt());
    }

    [Fact]
    public void NextMasked_MatchesTheOriginalGamesHelper()
    {
        var random = new GameRandom(42L);
        var original = new LegacyRandom(42L);

        for (int i = 0; i < 10_000; i++)
        {
            int modulus = 1 + (i % 97);
            Assert.Equal((original.nextInt() & 255) % modulus, random.NextMasked(255, modulus));
        }
    }

    [Fact]
    public void State_TakesOverTheOriginalGeneratorMidSequence()
    {
        var original = new LegacyRandom(7L);
        for (int i = 0; i < 123; i++)
        {
            original.nextInt();
        }

        var random = new GameRandom(0L) { State = original.State };

        for (int i = 0; i < 1_000; i++)
        {
            Assert.Equal(original.nextInt(), random.NextInt());
        }
    }
}
