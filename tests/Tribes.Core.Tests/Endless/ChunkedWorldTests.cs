using System.IO;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Tests.MultiTribe;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.Endless;

/// <summary>The world model on an endless (chunked) map: fog, sectors, slots that grow, saves.</summary>
public sealed class ChunkedWorldTests
{
    private static GameWorld NewWorld(int tribes = 2)
    {
        WorldMap map = WorldMap.Chunked();
        map.AddChunk(new ChunkCoord(0, 0), 1);
        map.AddChunk(new ChunkCoord(-1, 0), 1);
        return new GameWorld(MatchWorlds.Content, map, WorldLayout.ForTribes(tribes), new GameRandom(7)) { Mode = WorldMode.Match };
    }

    private static GameWorld RoundTrip(GameWorld world)
    {
        using var stream = new MemoryStream();
        WorldSnapshot.Save(world, stream);
        stream.Position = 0;
        return WorldSnapshot.Load(stream, MatchWorlds.Content);
    }

    [Fact]
    public void Fog_CoversNegativeCellsOfMadeLandOnly()
    {
        GameWorld world = NewWorld();
        FogOfWar fog = world.Fog(0);

        fog.SetVisible(new GridPoint(-3, 5), true);
        fog.SetVisible(new GridPoint(-200, 5), true);
        fog.ExploreVisible();
        fog.ClearVisible();

        Assert.True(fog.IsExplored(new GridPoint(-3, 5)));
        Assert.False(fog.IsVisible(new GridPoint(-3, 5)));
        Assert.False(fog.IsExplored(new GridPoint(-200, 5)));
        Assert.Equal(new GridRect(-3, 5, 1, 1), fog.ExploredBounds);
        Assert.Equal(world.Map.Size, fog.Size);
    }

    [Fact]
    public void Fog_ClearExploredForgetsEverything()
    {
        GameWorld world = NewWorld();
        world.Fog(1).SetExplored(new GridPoint(10, 10), true);

        world.Fog(1).ClearExplored();

        Assert.False(world.Fog(1).IsExplored(new GridPoint(10, 10)));
        Assert.True(world.Fog(1).ExploredBounds.IsEmpty);
    }

    [Fact]
    public void Sectors_HaveNoFixedNumberAndStartEmpty()
    {
        GameWorld world = NewWorld();
        SectorIndex index = world.Sectors;

        Assert.True(index.IsChunked);
        Assert.Equal(-1, index.SectorOf(-1));
        Assert.Equal(7, index.SectorOf(127));
        Assert.Equal(-1, index.Head(0, -2, 3));
        index.SetHead(0, -2, 3, 17);
        index.SetHead(4, 3, 0, 120);
        index.SetNext(4, 120, 5);

        Assert.Equal(17, index.Head(0, -2, 3));
        Assert.Equal(120, index.Head(4, 3, 0));
        Assert.Equal(5, index.Next(4, 120));
        index.EmptyHeads();
        Assert.Equal(-1, index.Head(0, -2, 3));
    }

    [Fact]
    public void AnimalSlots_Grow()
    {
        GameWorld world = NewWorld();
        int before = world.AnimalSlots;

        Animal added = world.AddAnimalSlot();

        Assert.Equal(before + 1, world.AnimalSlots);
        Assert.Equal(before, added.Index);
    }

    [Fact]
    public void CoupleLinks_AreWideAndCoupleSlotsUncapped()
    {
        GameWorld world = NewWorld(tribes: 40);
        Unit unit = world.Unit(1);

        unit.PairLink = (200 << 1) | unit.DelayedDeathBit;

        Assert.Equal(200 << 1, unit.PairLink & unit.CoupleLinkMask);
        Assert.NotEqual(0, unit.PairLink & unit.DelayedDeathBit);
        Assert.Equal(GameWorld.CoupleSlots, world.Couples.Count);
        for (int i = 0; i <= GameWorld.MostCoupleSlots; i++)
        {
            world.Couples[world.FreeCoupleSlot()].State = 1;
        }
        Assert.True(world.Couples.Count > GameWorld.MostCoupleSlots);
    }

    [Fact]
    public void AlarmMarks_KeepTribesBeyondTheBitField()
    {
        var alarm = new AlarmCall();

        alarm.MarkKnownTo(3);
        alarm.MarkKnownTo(45);
        alarm.MarkKnownTo(31);

        Assert.True(alarm.IsKnownTo(3));
        Assert.True(alarm.IsKnownTo(45));
        Assert.True(alarm.IsKnownTo(31));
        Assert.False(alarm.IsKnownTo(32));
        Assert.Equal(1 << 3, alarm.Spotted);
    }

    [Fact]
    public void Save_KeepsChunksFogSectorsAndGrownSlots()
    {
        GameWorld world = NewWorld(tribes: 3);
        world.Map.SetTile(new GridPoint(-5, 9), 77);
        world.Map.SetCell(new GridPoint(-5, 9), MapCell.Of(CellContent.Thicket));
        world.Map.PlaceResourceNode(2, new GridPoint(40, 40), 9);
        world.Fog(2).SetVisible(new GridPoint(-60, 60), true);
        world.Fog(2).ExploreVisible();
        world.Sectors.SetHead(1, -1, 2, 9);
        world.AddAnimalSlot().HitPoints = 3;
        world.Tribes[0].Alarms[0].MarkKnownTo(40);

        GameWorld loaded = RoundTrip(world);

        Assert.True(loaded.Map.IsChunked);
        Assert.Equal(world.Map.Bounds, loaded.Map.Bounds);
        Assert.Equal(77, loaded.Map.TileAt(new GridPoint(-5, 9)));
        Assert.Equal(CellContent.Thicket, loaded.Map.CellAt(new GridPoint(-5, 9)).Content);
        Assert.Equal(CellContent.ResourceNode, loaded.Map.CellAt(new GridPoint(40, 40)).Content);
        Assert.True(loaded.Fog(2).IsExplored(new GridPoint(-60, 60)));
        Assert.True(loaded.Fog(2).IsVisible(new GridPoint(-60, 60)));
        Assert.Equal(9, loaded.Sectors.Head(1, -1, 2));
        Assert.Equal(world.AnimalSlots, loaded.AnimalSlots);
        Assert.Equal(3, loaded.Animal(world.AnimalSlots - 1).HitPoints);
        Assert.True(loaded.Tribes[0].Alarms[0].IsKnownTo(40));
        Assert.Equal(WorldSnapshot.Hash(world), WorldSnapshot.Hash(loaded));
    }

    [Fact]
    public void Save_VisibleFogStillClearsAfterLoading()
    {
        GameWorld world = NewWorld();
        world.Fog(0).SetVisible(new GridPoint(1, 1), true);

        GameWorld loaded = RoundTrip(world);
        loaded.Fog(0).ClearVisible();

        Assert.False(loaded.Fog(0).IsVisible(new GridPoint(1, 1)));
    }

    [Fact]
    public void Hash_DiffersWhenACellDiffers()
    {
        GameWorld a = NewWorld();
        GameWorld b = NewWorld();
        ulong same = WorldSnapshot.Hash(b);

        b.Map.SetCell(new GridPoint(-64, 63), MapCell.Of(CellContent.Rocks));

        Assert.Equal(WorldSnapshot.Hash(a), same);
        Assert.NotEqual(WorldSnapshot.Hash(a), WorldSnapshot.Hash(b));
    }
}
