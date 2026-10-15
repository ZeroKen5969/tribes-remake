using System;
using System.IO;
using Tribes.Core.Content;
using Tribes.Legacy;
using Xunit;

namespace Tribes.Core.Tests;

/// <summary>The exported content (data/) against what the original game builds from its tables.</summary>
public sealed class ContentTests
{
    private static readonly string Root = TestPaths.RepoRoot();
    private static readonly GameContent Content = GameContent.Load(Path.Combine(Root, "data"));

    [Fact]
    public void Content_HasEveryScenarioAndMap()
    {
        Assert.Equal(12, Content.Missions.Count);
        Assert.Equal(10, Content.Maps.Count);
        Assert.Equal(20, Content.ResourceNodeKinds[1].MaxAmount);
    }

    [Fact]
    public void Save_ThenLoad_KeepsEverything()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tribes-content-" + Guid.NewGuid());
        try
        {
            Content.Save(directory);
            GameContent again = GameContent.Load(directory);

            Assert.Equal(Content.Missions.Values, again.Missions.Values);
            foreach (MapDefinition map in Content.Maps.Values)
            {
                MapDefinition copy = again.Maps[map.Id];
                Assert.Equal(map.Size, copy.Size);
                Assert.Equal(map.TileAt(new GridPoint(3, 7)), copy.TileAt(new GridPoint(3, 7)));
            }
            Assert.Equal(Content.Terrain[200], again.Terrain[200]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EveryMission_DecodesTheSameMapAsTheOriginal()
    {
        GameCanvas original = Tribes.Trace.Program.RunTo(Path.Combine(Root, "original", "game", "res"), 300, 0L,
            int.MaxValue);
        Assert.NotNull(original);
        TerrainCatalog terrain = Content.Terrain;

        foreach (MissionDefinition mission in Content.Missions.Values)
        {
            original.g_B((sbyte)mission.Id);
            MapDefinition map = Content.Maps[mission.MapId];

            Assert.Equal(map.Size, new GridSize(original.s_e, original.s_f));
            Assert.Equal(mission.PlayableSize, new GridSize(original.s_c, original.s_d));
            for (int y = 0; y < map.Size.Height; y++)
            {
                for (int x = 0; x < map.Size.Width; x++)
                {
                    int designed = map.TileAt(new GridPoint(x, y));
                    int decoded = original.bArr2_b[y][x] & 0xFF;
                    // The original draws a random variant of some tiles.
                    int variants = terrain.VariantTiles.Contains(designed) ? terrain.VariantCount : 1;
                    Assert.True(decoded >= designed && decoded < designed + variants,
                        $"mission {mission.Id} cell ({x},{y}): original {decoded}, data {designed}");
                }
            }
        }
    }
}
