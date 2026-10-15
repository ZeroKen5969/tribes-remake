using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Core.Tests.Systems;

/// <summary>Small worlds for edge-case tests of the rule systems.</summary>
internal static class TestWorlds
{
    /// <summary>
    /// A world of the original's layout on an empty map, with the real content tables, a little
    /// after the start of a mission (notices are not given at frame 0).
    /// </summary>
    public static GameWorld Empty(int width = 40, int height = 32, long seed = 1)
    {
        var size = new GridSize(width, height);
        return new GameWorld(LegacySamples.Content, new WorldMap(size, size), WorldLayout.Original, new GameRandom(seed)) { Frame = 1 };
    }
}
