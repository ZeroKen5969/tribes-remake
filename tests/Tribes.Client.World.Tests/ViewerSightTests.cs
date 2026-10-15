using System.Linq;
using Tribes.Client.World.Scene;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Client.World.Tests;

/// <summary>
/// Whose sight a view shows (<see cref="ViewerSight"/>): a player's own tribe and its allies, a
/// spectator every human player; which traps, markers and units that shows; and that it follows
/// alliances and tribes joining.
/// </summary>
public sealed class ViewerSightTests
{
    private static GameSimulation Match(int humans, int computers, params int[] teams)
    {
        var tribes = Enumerable.Range(0, humans).Select(h => new MatchTribe(TribeController.Human(h)))
            .Concat(Enumerable.Range(0, computers).Select(_ => new MatchTribe(TribeController.Computer())))
            .Select((t, i) => i < teams.Length ? t with { Team = teams[i] } : t)
            .ToArray();
        return GameSimulation.StartMatch(Repo.Instance.Content, new MatchSetup { Map = 0, Seed = 4, Tribes = tribes, HintsEnabled = false });
    }

    [Fact]
    public void APlayer_KnowsItsOwnTribeAndItsTeamMates_AndSeesThroughItsOwnFog()
    {
        GameWorld world = Match(2, 2, 1, 1, 2, 2).World;
        var sight = new ViewerSight(world, Viewpoint.Player(0));
        Assert.Equal(new[] { 0, 1 }, sight.Tribes);
        Assert.Equal(new[] { 0 }, sight.FogTribes);
        Assert.True(sight.IsOwn(0));
        Assert.False(sight.IsOwn(1));
        Assert.True(sight.ShowsMarksOf(0));
        Assert.False(sight.ShowsMarksOf(1));
        Assert.False(sight.Includes(2));
        Assert.True(sight.IsVisible(world.Tribes[1].Camp));
        Assert.Equal(world.Fog(0).ExploredBounds, sight.ExploredBounds);
    }

    [Fact]
    public void ASpectator_SeesEveryHumanPlayer_ButNotTheComputers()
    {
        GameWorld world = Match(3, 1).World;
        var sight = new ViewerSight(world, Viewpoint.Spectator(0));
        Assert.Equal(new[] { 0, 1, 2 }, sight.Tribes);
        Assert.Equal(new[] { 0, 1, 2 }, sight.FogTribes);
        Assert.False(sight.IsOwn(0));
        Assert.True(sight.ShowsMarksOf(2));
        Assert.False(sight.Includes(3));
        for (int t = 0; t < 3; t++)
        {
            Assert.True(sight.IsVisible(world.Tribes[t].Camp));
        }
        Assert.False(sight.IsExplored(world.Tribes[3].Camp));
        var player = new ViewerSight(world, Viewpoint.Player(0));
        Assert.False(player.IsExplored(world.Tribes[1].Camp));
    }

    [Fact]
    public void TheSight_FollowsAlliancesAndTribesJoining()
    {
        GameSimulation game = Match(2, 1);
        GameWorld world = game.World;
        var player = new ViewerSight(world, Viewpoint.Player(0));
        var spectator = new ViewerSight(world, Viewpoint.Spectator(0));
        Assert.False(player.Refresh());
        int version = player.Version;

        world.Relations.SetAlliance(0, 2, true);
        Assert.True(player.Refresh());
        Assert.Equal(new[] { 0, 2 }, player.Tribes);
        Assert.True(player.Includes(2));
        Assert.True(player.Version > version);
        Assert.False(spectator.Refresh());

        world.Relations.SetAlliance(0, 2, false);
        Assert.True(player.Refresh());
        Assert.Equal(new[] { 0 }, player.Tribes);

        int joined = game.AddTribe(TribeController.Human(5));
        Assert.True(joined > 0);
        Assert.True(spectator.Refresh());
        Assert.Contains(joined, spectator.Tribes);
        Assert.Contains(joined, spectator.FogTribes);
    }

    [Fact]
    public void Traps_ShowForTheTribesOfTheSight()
    {
        GameWorld world = Match(2, 1, 1, 1).World;
        var player = new ViewerSight(world, Viewpoint.Player(0));
        var spectator = new ViewerSight(world, Viewpoint.Spectator(0));
        const int Kind = CellCode.FirstTrapKind;
        Assert.Equal(CellClasses.OwnTrap, CellClasses.Of(new MapCell(CellContent.Trap, Kind, 1), player));
        Assert.Equal(CellClasses.OtherTrap, CellClasses.Of(new MapCell(CellContent.Trap, Kind, 2), player));
        Assert.Equal(CellClasses.OwnTrap, CellClasses.Of(new MapCell(CellContent.SpottedTrap, Kind, 1), player));
        Assert.Equal(CellClasses.OtherTrap, CellClasses.Of(new MapCell(CellContent.SpottedTrap, Kind, 2), player));
        Assert.Equal(CellClasses.PendingTrap, CellClasses.Of(new MapCell(CellContent.PendingTrap, Kind, 0), player));
        Assert.Equal(CellClasses.NotATrap, CellClasses.Of(new MapCell(CellContent.PendingTrap, Kind, 1), player));
        Assert.Equal(CellClasses.PendingTrap, CellClasses.Of(new MapCell(CellContent.PendingTrap, Kind, 1), spectator));
        Assert.Equal(CellClasses.OtherTrap, CellClasses.Of(new MapCell(CellContent.Trap, Kind, 2), spectator));

        Assert.Equal(CellClasses.OwnTrap, CellClasses.OfCode(CellCode.SpottedTrap(1, Kind), player));
        Assert.Equal(CellClasses.OtherTrap, CellClasses.OfCode(CellCode.SpottedTrap(2, Kind), player));
        Assert.Equal(CellClasses.OwnTrap, CellClasses.OfCode(CellCode.SpottedTrap(0, Kind), player));
        Assert.Equal(Kind + 1, CellClasses.KindOfCode(CellCode.SpottedTrap(2, Kind + 1)));
        Assert.Equal(Kind + 1, CellClasses.KindOfCode(CellCode.SpottedTrap(0, Kind + 1)));
        // The original's player alone sorts every code as before.
        Assert.Equal(CellClasses.OfCode(CellCode.SpottedTrap(0, Kind), 0), CellClasses.OfCode(CellCode.SpottedTrap(0, Kind), ViewerSight.Of(world, 0)));
        Assert.Equal(CellClasses.PendingTrap, CellClasses.OfCode(CellCode.PlayerPendingTrap, player));
    }

    [Fact]
    public void TheObjectLayer_DrawsWhatTheSightSees()
    {
        Repo repo = Repo.Instance;
        GameWorld world = Match(3, 0).World;
        GridPoint camp = world.Tribes[1].Camp;
        var view = new ViewRect(camp.X - 10, camp.Y - 8, 21, 16);
        var layer = new ObjectLayer(repo.Assets);
        var looks = new TribeLooks(0, world.Tribes.Count);
        int UnitsOfTribe1(ViewerSight sight)
        {
            var list = new SceneList();
            layer.Compose(new SceneRequest(world, view, new ScenePresentation(), WorldMarks.None, looks, false, sight), list);
            return list.Items.Count(i => i.SkinTribe == 1);
        }
        Assert.Equal(0, UnitsOfTribe1(new ViewerSight(world, Viewpoint.Player(0))));
        Assert.True(UnitsOfTribe1(new ViewerSight(world, Viewpoint.Spectator(0))) > 0);
        Assert.True(UnitsOfTribe1(new ViewerSight(world, Viewpoint.Player(1))) > 0);
    }

    [Fact]
    public void HiddenUnits_ShowOnlyToTheTribesThatKnowThem()
    {
        Repo repo = Repo.Instance;
        GameWorld world = Match(2, 1, 1, 1).World;
        foreach (Unit unit in world.UnitsOf(1))
        {
            unit.Status |= UnitStatus.Hidden;
        }
        GridPoint camp = world.Tribes[1].Camp;
        var view = new ViewRect(camp.X - 10, camp.Y - 8, 21, 16);
        var layer = new ObjectLayer(repo.Assets);
        var list = new SceneList();
        layer.Compose(new SceneRequest(world, view, new ScenePresentation(), WorldMarks.None, new TribeLooks(0, 3), false,
            new ViewerSight(world, Viewpoint.Player(0))), list);
        Assert.Contains(list.Items, i => i.SkinTribe == 1);
        foreach (Unit unit in world.UnitsOf(2))
        {
            unit.Status |= UnitStatus.Hidden;
            (unit.X, unit.Y) = (camp.X, camp.Y + 2);
        }
        world.Fog(0).SetVisible(new GridPoint(camp.X, camp.Y + 2), true);
        layer.Compose(new SceneRequest(world, view, new ScenePresentation(), WorldMarks.None, new TribeLooks(0, 3), false,
            new ViewerSight(world, Viewpoint.Player(0))), list);
        Assert.DoesNotContain(list.Items, i => i.SkinTribe == 2);
    }

    [Fact]
    public void OutOfSight_ABuildingShowsAsLastSeen_AndOneNeverSeenDoesNotShow()
    {
        Repo repo = Repo.Instance;
        GameSimulation game = Match(2, 1);
        GameWorld world = game.World;
        world.Relations.SetAlliance(0, 1, true);
        StepSight(game);
        world.Relations.SetAlliance(0, 1, false);
        StepSight(game);
        GridPoint camp = world.Tribes[1].Camp;
        var view = new ViewRect(camp.X - 10, camp.Y - 8, 21, 16);
        var layer = new ObjectLayer(repo.Assets);
        var terrain = new TerrainLayer(repo.Assets);
        var sight = new ViewerSight(world, Viewpoint.Player(0));
        string Picture()
        {
            var list = new SceneList();
            layer.Compose(new SceneRequest(world, view, new ScenePresentation(), WorldMarks.None, new TribeLooks(0, world.Tribes.Count), false, sight), list);
            return string.Join(";", list.Items.Select(i => i.Part.ToString()));
        }
        long Ground() => TerrainLayer.Signature(world, view.Column, view.Row, view.Column + view.Columns, view.Row + view.Rows, sight);
        string remembered = Picture();
        long rememberedGround = Ground();
        Building theirs = world.BuildingsOf(1).First(b => b.InUse);
        theirs.Level++;
        Assert.Equal(remembered, Picture());
        Assert.Equal(rememberedGround, Ground());
        foreach (Building b in world.BuildingsOf(1))
        {
            b.HitPoints = 0;
        }
        Assert.Equal(remembered, Picture());
        Assert.Equal(rememberedGround, Ground());
        world.Relations.SetAlliance(0, 1, true);
        StepSight(game);
        world.Relations.SetAlliance(0, 1, false);
        StepSight(game);
        Assert.NotEqual(remembered, Picture());
        Assert.NotEqual(rememberedGround, Ground());

        GridPoint other = world.Tribes[2].Camp;
        var otherView = new ViewRect(other.X - 10, other.Y - 8, 21, 16);
        for (int y = otherView.Row; y < otherView.Row + otherView.Rows; y++)
        {
            for (int x = otherView.Column; x < otherView.Column + otherView.Columns; x++)
            {
                world.Fog(0).SetExplored(new GridPoint(x, y), true);
            }
        }
        view = otherView;
        string unknown = Picture();
        long unknownGround = Ground();
        foreach (Building b in world.BuildingsOf(2))
        {
            b.HitPoints = 0;
        }
        Assert.Equal(unknown, Picture());
        Assert.Equal(unknownGround, Ground());
    }

    private static void StepSight(GameSimulation game)
    {
        for (int i = 0; i < 4; i++)
        {
            game.Step();
        }
    }

    [Fact]
    public void TheMinimap_PaintsWhatTheSpectatorSees()
    {
        GameWorld world = Match(2, 1).World;
        var painter = new MinimapPainter(t => 0x123456u * (uint)(t + 1) & 0xFFFFFF);
        CellRect area = MapArea.Bounds(world.Map);
        MinimapLayout layout = MinimapLayout.Fit(area, area.Width, area.Height, area.Left + (area.Width / 2), area.Top + (area.Height / 2));
        byte[] spectator = new byte[layout.PictureWidth * layout.PictureHeight * 4];
        byte[] player = new byte[spectator.Length];
        painter.PaintLand(world, new ViewerSight(world, Viewpoint.Spectator(0)), layout, spectator, 0, layout.PictureHeight);
        painter.PaintLand(world, new ViewerSight(world, Viewpoint.Player(0)), layout, player, 0, layout.PictureHeight);
        GridPoint camp = world.Tribes[1].Camp;
        (int X, int Y)? pixel = layout.PixelOfCell(camp.X, camp.Y);
        Assert.NotNull(pixel);
        int i = ((pixel.Value.Y * layout.PictureWidth) + pixel.Value.X) * 4;
        uint Colour(byte[] rgba) => ((uint)rgba[i] << 16) | ((uint)rgba[i + 1] << 8) | rgba[i + 2];
        Assert.Equal(MinimapPainter.Unexplored, Colour(player));
        Assert.NotEqual(MinimapPainter.Unexplored, Colour(spectator));
    }
}
