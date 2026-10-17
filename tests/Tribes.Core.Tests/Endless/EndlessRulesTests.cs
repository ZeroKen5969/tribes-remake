using System;
using System.IO;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.Endless;

/// <summary>The endless rules file, the endless content, wildlife, pacing and the camp sites' last resort.</summary>
public sealed class EndlessRulesTests
{
    private static EndlessRules Rules => EndlessWorlds.Content.Endless!;

    [Fact]
    public void Rules_SaveAndLoadAgain()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tribes-endless-" + Guid.NewGuid());
        try
        {
            Rules.Save(directory);
            EndlessRules? again = EndlessRules.Load(directory);

            Assert.NotNull(again);
            Assert.Equal(Rules.Pacing, again!.Pacing);
            Assert.Equal(Rules.Alliance.Goods, again.Alliance.Goods);
            Assert.Null(EndlessRules.Load(Path.Combine(directory, "none")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Rules_RefuseNumbersOutOfRange()
    {
        Assert.Throws<InvalidDataException>(() => (Rules with { Land = Rules.Land with { ReachCells = 0 } }).Checked());
        Assert.Throws<InvalidDataException>(() => (Rules with { Camps = Rules.Camps with { CandidateStep = 0 } }).Checked());
        Assert.Throws<InvalidDataException>(() => (Rules with { NewTribes = Rules.NewTribes with { DistanceFromPeopleCamps = -1 } }).Checked());
        Assert.Throws<InvalidDataException>(() => (Rules with { Wildlife = Rules.Wildlife with { HabitatChancePercent = 401 } }).Checked());
        Assert.Throws<InvalidDataException>(() => (Rules with { Pacing = Rules.Pacing with { AttackersPerTarget = 0 } }).Checked());
        Assert.Throws<InvalidDataException>(() => (Rules with { Alliance = Rules.Alliance with { GoodsCap = 0 } }).Checked());
    }

    [Fact]
    public void Rules_RefuseAFileThatIsNotJson()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tribes-endless-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, EndlessRules.DirectoryName));
        try
        {
            File.WriteAllText(Path.Combine(directory, EndlessRules.DirectoryName, EndlessRules.FileName), "{ \"mission\": 7, \"bad\": 1 }");
            Assert.Throws<InvalidDataException>(() => EndlessRules.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Content_WithoutEndlessRules_CannotStartAnEndlessWorld()
    {
        GameContent bare = new(EndlessWorlds.Content.Missions.Values, EndlessWorlds.Content.Maps.Values, EndlessWorlds.Content.Terrain,
            EndlessWorlds.Content.ResourceNodeKinds.Values, EndlessWorlds.Content.Rules);

        Assert.NotNull(EndlessStart.Problem(bare, EndlessWorlds.Setup(1, 0)));
        Assert.Throws<InvalidDataException>(() => EndlessContent.For(bare));
    }

    [Fact]
    public void Wildlife_HabitatsGetAnimalsNearTheTribes()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 2, seed: 15);
        GameWorld world = game.World;
        Assert.NotEmpty(world.Endless!.Habitats);

        for (int tick = 0; tick < 6000; tick++)
        {
            game.Step();
        }

        Assert.Contains(world.Endless.Habitats, h => h.Animals > 0);
        Assert.All(world.Endless.Habitats, h => Assert.InRange(h.Animals, 0, Rules.Wildlife.AnimalsPerHabitat));
        Animal born = Enumerable.Range(0, world.AnimalSlots).Select(world.Animal).First(a => a.InUse);
        Assert.InRange(born.SpawnZone, 0, world.Endless.Habitats.Count - 1);
        Assert.Equal(world.Endless.Habitats.Sum(h => h.Animals),
            Enumerable.Range(0, world.AnimalSlots).Select(world.Animal).Count(a => a.InUse));
    }

    [Fact]
    public void Pacing_ProtectsThePeopleAtTheStartAndNewcomers()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 2, seed: 16);
        GameWorld world = game.World;

        Assert.Equal(Rules.Pacing.OpeningGraceFrames, world.Settings.OpeningGraceFrames);
        Assert.False(EndlessPacing.MayTarget(world, 1, 0, 100));
        Assert.False(EndlessPacing.MayTarget(world, 1, 2, 100));
        Assert.False(EndlessPacing.MaySendWaves(world, 1));
        Assert.False(EndlessPacing.MayDefend(world, 1));
        world.Frame = Rules.Pacing.OpeningGraceFrames;
        Assert.True(EndlessPacing.MayTarget(world, 1, 0, 100));
        Assert.True(EndlessPacing.MayTarget(world, 1, 2, 100));
        Assert.False(EndlessPacing.MayTarget(world, 1, 2, (long)(Rules.Pacing.AttackReachCells + 1) * (Rules.Pacing.AttackReachCells + 1)));
        Assert.True(EndlessPacing.MayDefend(world, 1));

        int newcomer = game.AddTribe(TribeController.Computer());
        Assert.False(EndlessPacing.MayTarget(world, 1, newcomer, 100));
        Assert.False(EndlessPacing.MaySendWaves(world, newcomer));
        Assert.Equal(0, EndlessPacing.ComputerFrames(world, newcomer));
        world.Frame += Rules.Pacing.NewcomerGraceFrames;
        Assert.True(EndlessPacing.MaySendWaves(world, newcomer));
        Assert.True(EndlessPacing.MayTarget(world, 1, newcomer, 100));
    }

    [Fact]
    public void Pacing_ShiftsEachComputersTimetableInEndlessWorldsOnly()
    {
        GameWorld world = EndlessWorlds.Start(humans: 1, computers: 64, seed: 18).World;
        GameWorld versus = GameSimulation.StartMatch(MultiTribe.MatchWorlds.Content, MultiTribe.MatchWorlds.Setup(1, 3, MultiTribe.MatchWorlds.Controllers(1, 3))).World;
        const int IntruderMask = 63;

        var phases = Enumerable.Range(1, 64).Select(t => EndlessPacing.Clock(world, t) & IntruderMask).ToHashSet();

        Assert.Equal(64, phases.Count);
        Assert.All(Enumerable.Range(0, 4), t => Assert.Equal(versus.Frame, EndlessPacing.Clock(versus, t)));
    }

    [Fact]
    public void Pacing_LimitsTheAttackersOfOneTribe()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 4, seed: 17);
        GameWorld world = game.World;
        world.Frame = Rules.Pacing.OpeningGraceFrames;
        for (int t = 1; t <= Rules.Pacing.AttackersPerTarget; t++)
        {
            world.Tribes[t].Ai.WaveTarget = 0;
            world.Tribes[t].Ai.WaveTribe = 0;
        }

        Assert.False(EndlessPacing.MayTarget(world, 4, 0, 100));
        Assert.True(EndlessPacing.MayTarget(world, 1, 0, 100));
    }

    [Fact]
    public void Pacing_LeavesOtherWorldsAlone()
    {
        GameSimulation versus = GameSimulation.StartMatch(EndlessWorlds.Content,
            MultiTribe.MatchWorlds.Setup(0, 2, MultiTribe.MatchWorlds.Controllers(1, 1)));

        Assert.True(EndlessPacing.MayTarget(versus.World, 1, 0, long.MaxValue));
        Assert.True(EndlessPacing.MaySendWaves(versus.World, 1));
        Assert.Equal(versus.World.Frame, EndlessPacing.ComputerFrames(versus.World, 1));
    }

    [Fact]
    public void CampSites_ForceIn_ClearsASiteBesideTheTopConnector()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 0, seed: 18);
        var far = new ChunkCoord(30, 30);
        game.GenerateLand(far.Cells);

        GridPoint? site = game.Rules.CampSites.ForceIn(far);

        Assert.NotNull(site);
        Assert.True(far.Cells.Contains(site!.Value) || game.World.Map.InPlay(site.Value));
        Assert.True(game.World.Map.CellAt(site.Value).IsEmpty);
    }

    [Fact]
    public void ChunkGrid_GrowsInEveryDirection()
    {
        WorldMap map = WorldMap.Chunked();
        foreach (ChunkCoord chunk in new[] { new ChunkCoord(0, 0), new ChunkCoord(40, 0), new ChunkCoord(0, -40), new ChunkCoord(-40, 40) })
        {
            map.AddChunk(chunk, 1);
        }

        Assert.NotNull(map.Chunk(new ChunkCoord(-40, 40)));
        Assert.NotNull(map.Chunk(new ChunkCoord(40, 0)));
        Assert.Null(map.Chunk(new ChunkCoord(39, 0)));
        Assert.Equal(GridRect.FromEdges(-40 * 64, -40 * 64, 41 * 64, 41 * 64), map.Bounds);
        map.Clear();
        Assert.Empty(map.Chunks);
        Assert.True(map.Bounds.IsEmpty);
    }
}
