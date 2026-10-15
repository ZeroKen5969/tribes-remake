using System.IO;
using System.Linq;
using System.Text;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// In a match each human tribe remembers the buildings it has seen as it last saw them: one put up
/// out of its sight is not known, one destroyed out of its sight still stands in its memory until
/// it sees the place again; allies share what they remember; saves keep it (older saves get it from
/// the explored land); the campaign keeps none.
/// </summary>
public sealed class BuildingMemoryTests
{
    private const int SightFrames = 4;

    private static GameSimulation NewMatch(int humans, int computers) =>
        GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, 2, MatchWorlds.Controllers(humans, computers)));

    private static void StepSight(GameSimulation game)
    {
        for (int i = 0; i < SightFrames; i++)
        {
            game.Step();
        }
    }

    /// <summary>Lets <paramref name="viewer"/> see <paramref name="tribe"/>'s camp for one sight update (an alliance made and ended).</summary>
    private static void LookAtCamp(GameSimulation game, int viewer, int tribe)
    {
        game.World.Relations.SetAlliance(viewer, tribe, true);
        StepSight(game);
        game.World.Relations.SetAlliance(viewer, tribe, false);
        StepSight(game);
    }

    [Fact]
    public void ABuildingSeen_IsRememberedAsItWas_AndOneBuiltOutOfSightIsNot()
    {
        GameSimulation game = NewMatch(2, 1);
        GameWorld world = game.World;
        BuildingMemory memory = world.Fog(0).Buildings;
        Building theirs = world.BuildingsOf(1).First(b => b.InUse);
        Assert.False(memory.TryGet(theirs.Id, out _));
        Assert.Contains(memory.Buildings, b => b.Tribe == 0);

        LookAtCamp(game, 0, 1);
        Assert.True(memory.TryGet(theirs.Id, out RememberedBuilding seen));
        Assert.Equal((theirs.X, theirs.Y, theirs.Kind, theirs.Level), (seen.X, seen.Y, seen.Kind, seen.Level));
        Assert.False(world.Fog(0).AnyVisible(seen.Footprint(world.Content)));

        int oldLevel = theirs.Level;
        theirs.Level = oldLevel + 1;
        StepSight(game);
        Assert.True(memory.TryGet(theirs.Id, out seen));
        Assert.Equal(oldLevel, seen.Level);
        Assert.DoesNotContain(memory.Buildings, known => known.Tribe == 2);
        Building unseen = world.BuildingsOf(2).First(b => b.InUse);
        (unseen.X, unseen.Y) = (theirs.X + 6, theirs.Y);
        StepSight(game);
        Assert.False(memory.TryGet(unseen.Id, out _));
    }

    [Fact]
    public void ABuildingDestroyedOutOfSight_StaysUntilThePlaceIsSeenAgain()
    {
        GameSimulation game = NewMatch(2, 1);
        GameWorld world = game.World;
        BuildingMemory memory = world.Fog(0).Buildings;
        LookAtCamp(game, 0, 1);
        Building theirs = world.BuildingsOf(1).First(b => b.InUse && memory.TryGet(b.Id, out _));
        theirs.HitPoints = 0;
        StepSight(game);
        Assert.True(memory.TryGet(theirs.Id, out _));

        LookAtCamp(game, 0, 1);
        Assert.False(memory.TryGet(theirs.Id, out _));
    }

    [Fact]
    public void Allies_ShareWhatTheyRemember()
    {
        GameSimulation game = NewMatch(3, 0);
        GameWorld world = game.World;
        LookAtCamp(game, 1, 2);
        Building third = world.BuildingsOf(2).First(b => b.InUse);
        Assert.True(world.Fog(1).Buildings.TryGet(third.Id, out RememberedBuilding seenByOne));
        Assert.False(world.Fog(0).Buildings.TryGet(third.Id, out _));

        world.Relations.SetAlliance(0, 1, true);
        StepSight(game);
        Assert.True(world.Fog(0).Buildings.TryGet(third.Id, out RememberedBuilding shared));
        Assert.Equal(seenByOne, shared);
    }

    [Fact]
    public void TheMemory_IsSaved_AndAnOlderSaveRemembersWhatItsTribesExplored()
    {
        GameSimulation game = NewMatch(2, 1);
        GameWorld world = game.World;
        LookAtCamp(game, 0, 1);
        GameWorld loaded = GameSimulation.Load(MatchWorlds.Content, game.Save()).World;
        Assert.Equal(world.Fog(0).Buildings.Buildings, loaded.Fog(0).Buildings.Buildings);
        Assert.Equal(game.StateHash(), GameSimulation.Load(MatchWorlds.Content, game.Save()).StateHash());

        GameWorld older = LoadAsFormat(world, StateExchange.CombatFormat);
        foreach (Building b in world.BuildingsOf(1).Where(b => b.InUse))
        {
            Assert.True(older.Fog(0).Buildings.TryGet(b.Id, out _));
        }
        Assert.DoesNotContain(older.Fog(0).Buildings.Buildings, b => b.Tribe == 2);
    }

    [Fact]
    public void TheCampaign_RemembersNothing()
    {
        GameSimulation game = GameSimulation.StartMission(MatchWorlds.Content, 2, 0);
        for (int i = 0; i < 40; i++)
        {
            game.Step();
        }
        Assert.Equal(0, game.World.Fog(0).Buildings.Count);
    }

    /// <summary>The world written in the older <paramref name="format"/> and read back.</summary>
    private static GameWorld LoadAsFormat(GameWorld world, int format)
    {
        const int Magic = 0x57425254, FixedMap = 0;
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(format);
            WorldLayout layout = world.Layout;
            foreach (int value in new[]
            {
                layout.Tribes, layout.UnitsPerTribe, layout.BuildingsPerTribe, layout.Animals, layout.FogSize.Width, layout.FogSize.Height,
                world.Map.Size.Width, world.Map.Size.Height, world.Map.ResourceNodes.Count, FixedMap,
            })
            {
                writer.Write(value);
            }
            world.Exchange(new StateWriter(writer) { Format = format });
        }
        bytes.Position = 0;
        return WorldSnapshot.Load(bytes, MatchWorlds.Content);
    }
}
