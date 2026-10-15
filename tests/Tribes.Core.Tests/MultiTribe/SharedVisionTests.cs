using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Match;
using Tribes.Core.Tests.Endless;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// Allies share vision in matches and endless worlds: each sees what the other's units and buildings
/// see, allied human tribes share the land they explored, explored land stays explored when the
/// alliance ends, and the rules that read a tribe's fog count what its allies show it.
/// </summary>
public sealed class SharedVisionTests
{
    /// <summary>Frames between two sight updates (the rules recompute sight every fourth frame).</summary>
    private const int SightFrames = 4;

    private static GameSimulation NewMatch(int humans, int computers, int[]? teams, long seed = 2) =>
        GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(0, seed, MatchWorlds.Controllers(humans, computers), teams));

    private static void StepSight(GameSimulation game)
    {
        for (int i = 0; i < SightFrames; i++)
        {
            game.Step();
        }
    }

    [Fact]
    public void TeamMates_SeeEachOthersCamp_ButNotTheEnemys()
    {
        GameSimulation game = NewMatch(2, 1, [1, 1, -1]);
        GameWorld world = game.World;
        Assert.True(world.Fog(0).IsVisible(world.Tribes[1].Camp));
        Assert.True(world.Fog(1).IsVisible(world.Tribes[0].Camp));
        Assert.False(world.Fog(0).IsExplored(world.Tribes[2].Camp));
        Assert.False(world.Fog(1).IsExplored(world.Tribes[2].Camp));
    }

    [Fact]
    public void WithoutAlliance_NobodySeesTheOthersCamp()
    {
        GameSimulation game = NewMatch(2, 1, teams: null);
        GameWorld world = game.World;
        Assert.False(world.Fog(0).IsExplored(world.Tribes[1].Camp));
        Assert.False(world.Fog(1).IsExplored(world.Tribes[0].Camp));
    }

    [Fact]
    public void ComputerTeamMate_ShowsItsSightToTheHuman()
    {
        GameSimulation game = NewMatch(1, 2, [1, 1, 2]);
        GameWorld world = game.World;
        Assert.True(world.Fog(0).IsVisible(world.Tribes[1].Camp));
        Assert.False(world.Fog(0).IsExplored(world.Tribes[2].Camp));
    }

    [Fact]
    public void TeamMates_ShareEveryExploredCell()
    {
        GameSimulation game = NewMatch(2, 2, [1, 1, 2, 2], seed: 7);
        for (int tick = 0; tick < 1200; tick++)
        {
            game.Step(MatchWorlds.Orders(game, game.Tick, 7));
        }
        StepSight(game);
        GameWorld world = game.World;
        GridSize size = world.Fog(0).Size;
        int shared = 0;
        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                var cell = new GridPoint(x, y);
                Assert.Equal(world.Fog(0).IsExplored(cell), world.Fog(1).IsExplored(cell));
                shared += world.Fog(0).IsExplored(cell) ? 1 : 0;
            }
        }
        Assert.True(shared > 0);
    }

    [Fact]
    public void AllianceMadeInGame_SharesSight_AndEndingItKeepsTheExploredLand()
    {
        GameSimulation game = NewMatch(2, 1, teams: null);
        GameWorld world = game.World;
        GridPoint otherCamp = world.Tribes[1].Camp;
        Assert.False(world.Fog(0).IsExplored(otherCamp));

        world.Relations.SetAlliance(0, 1, true);
        StepSight(game);
        Assert.True(world.Fog(0).IsVisible(otherCamp));
        Assert.Equal(FogState.Visible, game.Rules.Passability.FogAt(otherCamp, 0));

        world.Relations.SetAlliance(0, 1, false);
        StepSight(game);
        Assert.False(world.Fog(0).IsVisible(otherCamp));
        Assert.True(world.Fog(0).IsExplored(otherCamp));
        Assert.Equal(FogState.Explored, game.Rules.Passability.FogAt(otherCamp, 0));
    }

    [Fact]
    public void AnAllysSight_IsNotPassedOn_ButItsExploredLandIs()
    {
        GameSimulation game = NewMatch(3, 0, teams: null);
        GameWorld world = game.World;
        world.Relations.SetAlliance(0, 1, true);
        world.Relations.SetAlliance(1, 2, true);
        StepSight(game);
        GridPoint farCamp = world.Tribes[2].Camp;
        Assert.True(world.Fog(1).IsVisible(farCamp));
        Assert.False(world.Fog(0).IsVisible(farCamp));
        Assert.True(world.Fog(0).IsExplored(farCamp));
    }

    [Fact]
    public void ARevealBetweenSightUpdates_ReachesTheHumanAllies_ButNotTheExploredLayerOfOthers()
    {
        GameSimulation game = NewMatch(3, 0, [1, 1, -1]);
        GameWorld world = game.World;
        GridPoint cell = world.Tribes[2].Camp;
        Assert.False(world.Fog(0).IsVisible(cell));
        game.Rules.Sight.RevealDisc(1, Tribes.Core.Systems.Vision.FogLayer.Visible, cell.X, cell.Y, 2);
        Assert.True(world.Fog(1).IsVisible(cell));
        Assert.True(world.Fog(0).IsVisible(cell));
        game.Rules.Sight.RevealRect(1, Tribes.Core.Systems.Vision.FogLayer.Explored, cell.X + 5, cell.Y, 1, 1);
        Assert.True(world.Fog(1).IsExplored(new GridPoint(cell.X + 5, cell.Y)));
        Assert.False(world.Fog(0).IsExplored(new GridPoint(cell.X + 5, cell.Y)));
    }

    [Fact]
    public void Rules_CountWhatAnAllyShows()
    {
        GameSimulation allied = NewMatch(2, 1, [1, 1, -1]);
        GameSimulation alone = NewMatch(2, 1, teams: null);
        foreach ((GameSimulation game, bool seen) in new[] { (allied, true), (alone, false) })
        {
            GameWorld world = game.World;
            GridPoint camp = world.Tribes[1].Camp;
            Unit enemy = world.UnitsOf(2).First(u => u.InUse);
            CellProbe probe = new CellClassifier(world).Probe(CellCode.OfUnit(enemy.Id), camp, 0);
            Assert.Equal(seen ? CellCategories.Unit : CellCategories.Empty, probe.Category);
            GridPoint free = FreeVisibleCellNear(world, camp, 1);
            Assert.Equal(seen, game.Rules.BuildingPlacement.FootprintFree(BuildingKindNumbers.FirstTrap, free, inView: true, tribe: 0));
        }
    }

    [Fact]
    public void SharedVision_IsDeterministic_AndSurvivesSaveAndLoad()
    {
        MatchSetup setup = MatchWorlds.Setup(1, 5, MatchWorlds.Controllers(3, 1), [1, 1, -1, 2]) with { EndsWithLastSide = false };
        GameSimulation game = GameSimulation.StartMatch(MatchWorlds.Content, setup);
        GameSimulation? loaded = null;
        while (game.Tick < 2400)
        {
            if (game.Tick == 600)
            {
                game.World.Relations.SetAlliance(2, 0, true);
            }
            if (game.Tick == 1201)
            {
                loaded = GameSimulation.Load(MatchWorlds.Content, game.Save());
            }
            if (game.Tick == 1800)
            {
                game.World.Relations.SetAlliance(2, 0, false);
                loaded!.World.Relations.SetAlliance(2, 0, false);
            }
            TickInput input = MatchWorlds.Orders(game, game.Tick, 5);
            game.Step(input);
            loaded?.Step(input);
            Assert.Equal(game.StateHash(), loaded?.StateHash() ?? game.StateHash());
        }
        Assert.NotNull(loaded);
    }

    [Fact]
    public void EndlessAllies_ShareSightAndExploredChunks()
    {
        GameSimulation game = EndlessWorlds.Start(2, 0);
        GameWorld world = game.World;
        GridPoint otherCamp = world.Tribes[1].Camp;
        Assert.False(world.Fog(0).IsExplored(otherCamp));
        world.Relations.SetAlliance(0, 1, true);
        StepSight(game);
        Assert.True(world.Fog(0).IsVisible(otherCamp));
        GridRect explored = world.Fog(1).ExploredBounds;
        for (int y = explored.Top; y < explored.Bottom; y++)
        {
            for (int x = explored.Left; x < explored.Right; x++)
            {
                var cell = new GridPoint(x, y);
                Assert.True(!world.Fog(1).IsExplored(cell) || world.Fog(0).IsExplored(cell), $"cell {cell}");
            }
        }
        world.Relations.SetAlliance(0, 1, false);
        StepSight(game);
        Assert.False(world.Fog(0).IsVisible(otherCamp));
        Assert.True(world.Fog(0).IsExplored(otherCamp));
    }

    [Fact]
    public void AFightBetweenOtherTribes_DoesNotShowTheAttackers()
    {
        GameSimulation game = NewMatch(1, 2, teams: null);
        GameWorld world = game.World;
        (Unit attacker, GridPoint at) = AttackerOutOfSight(world, 1, 0);
        Unit victim = world.UnitsOf(2).First(u => u.InUse);
        Strike(attacker, CellCode.OfUnit(victim.Id), victim.Id);
        game.Rules.Sight.UpdateSight();
        Assert.False(world.Fog(0).IsVisible(at));
        Strike(attacker, CellCode.OfBuilding(world.BuildingsOf(2).First(b => b.InUse).Id), world.BuildingsOf(2).First(b => b.InUse).Id);
        game.Rules.Sight.UpdateSight();
        Assert.False(world.Fog(0).IsVisible(at));
    }

    [Fact]
    public void AnEnemyAttackingTheTribe_OrItsAllies_IsSeenWhereItStands()
    {
        GameSimulation game = NewMatch(2, 2, [1, 1, -1, 1]);
        GameWorld world = game.World;
        (Unit attacker, GridPoint at) = AttackerOutOfSight(world, 2, 0, 1);
        Unit own = world.UnitsOf(0).First(u => u.InUse);
        Strike(attacker, CellCode.OfUnit(own.Id), own.Id);
        game.Rules.Sight.UpdateSight();
        Assert.True(world.Fog(0).IsVisible(at));
        Assert.True(world.Fog(1).IsVisible(at));

        Building allysHut = world.BuildingsOf(1).First(b => b.InUse);
        Strike(attacker, CellCode.OfBuilding(allysHut.Id), allysHut.Id);
        game.Rules.Sight.UpdateSight();
        Assert.True(world.Fog(0).IsVisible(at));

        Unit computerMate = world.UnitsOf(3).First(u => u.InUse);
        Strike(attacker, CellCode.OfUnit(computerMate.Id), computerMate.Id);
        game.Rules.Sight.UpdateSight();
        Assert.True(world.Fog(0).IsVisible(at));
    }

    /// <summary>A living unit of <paramref name="tribe"/> moved to a cell in play none of <paramref name="viewers"/> sees.</summary>
    private static (Unit Unit, GridPoint Cell) AttackerOutOfSight(GameWorld world, int tribe, params int[] viewers)
    {
        Unit unit = world.UnitsOf(tribe).First(u => u.InUse);
        GridRect bounds = world.Map.Bounds;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                var cell = new GridPoint(x, y);
                if (world.Map.InPlay(cell) && viewers.All(v => !world.Fog(v).IsExplored(cell)) && FarFromAll(world, cell, tribe))
                {
                    (unit.X, unit.Y) = (x, y);
                    return (unit, cell);
                }
            }
        }
        throw new Xunit.Sdk.XunitException("no cell out of sight");
    }

    /// <summary>Whether no unit or building of another tribe than <paramref name="tribe"/> stands within 15 cells of <paramref name="cell"/>.</summary>
    private static bool FarFromAll(GameWorld world, GridPoint cell, int tribe)
    {
        for (int t = 0; t < world.Tribes.Count; t++)
        {
            if (t != tribe && world.Tribes[t].Camp is var camp && System.Math.Abs(camp.X - cell.X) + System.Math.Abs(camp.Y - cell.Y) < 30)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Puts <paramref name="attacker"/> in the first frame of a melee blow at the target of <paramref name="code"/>.</summary>
    private static void Strike(Unit attacker, int code, int target)
    {
        const int MeleeAttack = 0, BlowAnimation = 23;
        attacker.Action = MeleeAttack;
        attacker.Animation = BlowAnimation;
        attacker.TargetCellCode = code;
        attacker.TargetIndex = target;
    }

    /// <summary>An empty cell in play next to <paramref name="near"/> that <paramref name="viewer"/> sees now.</summary>
    private static GridPoint FreeVisibleCellNear(GameWorld world, GridPoint near, int viewer)
    {
        for (int radius = 1; radius < 12; radius++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var cell = new GridPoint(near.X + dx, near.Y + dy);
                    if (world.Map.InPlay(cell) && world.Map.CellAt(cell).IsEmpty && world.Fog(viewer).IsVisible(cell))
                    {
                        return cell;
                    }
                }
            }
        }
        throw new Xunit.Sdk.XunitException($"no free visible cell near {near}");
    }
}
