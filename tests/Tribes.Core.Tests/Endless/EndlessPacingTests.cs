using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Endless;

/// <summary>A made-up player who builds up the camp: now and then a building near it, and a few adults sent to build it.</summary>
internal static class BuildingPlayer
{
    /// <summary>The buildings the player orders, over and over: huts, kitchen, store, armoury, tower.</summary>
    private static readonly int[] Plan = { 2, 4, 2, 6, 0, 2, 11, 9 };

    public static TickInput Orders(GameSimulation game, long tick)
    {
        GameWorld world = game.World;
        var orders = new List<GameOrder>();
        foreach (TribeState tribe in world.Tribes)
        {
            if (tribe.Controller.Kind != TribeControl.Human || tribe.OutAtFrame >= 0)
            {
                continue;
            }
            if (TribeRoles.AwaitsNewChief(world, tribe.Index) && Adults(world, tribe.Index).FirstOrDefault() is int chief and > 0)
            {
                orders.Add(new AppointChief(tribe.Index, chief));
            }
            if (tick % 600 == 300 && Site(world, tribe.Index) == 0)
            {
                int kind = Plan[(int)(tick / 600) % Plan.Length];
                if (Place(game, tribe.Index, kind) is GridPoint origin)
                {
                    orders.Add(new PlaceBuilding(tribe.Index, kind, origin));
                }
            }
            if (tick % 200 == 0 && Site(world, tribe.Index) is int site and > 0)
            {
                int[] builders = Adults(world, tribe.Index).Take(4).ToArray();
                if (builders.Length > 0)
                {
                    Building building = world.Building(site);
                    orders.Add(new MoveUnits(tribe.Index, new UnitSet(builders), new GridPoint(building.X, building.Y),
                        Maps.CellCode.OfBuilding(site), Hurry: false));
                }
            }
        }
        return new TickInput { Orders = orders.Select(PlayerOrder.From).ToArray() };
    }

    private static IEnumerable<int> Adults(GameWorld world, int tribe) =>
        world.UnitsOf(tribe).Select(u => u.Id)
            .Where(id => world.Unit(id).HitPoints > 0 && world.Unit(id).Age >= 3 && world.Unit(id).BuildingSlot == 0);

    private static int Site(GameWorld world, int tribe) =>
        world.BuildingsOf(tribe).Select(b => b.Id)
            .FirstOrDefault(id => world.Building(id).HitPoints > 0 && Tribes.Core.Systems.Entities.BuildingKindNumbers.IsSite(world.Building(id).Kind));

    private static GridPoint? Place(GameSimulation game, int tribe, int kind)
    {
        GridPoint camp = game.World.Tribes[tribe].Camp;
        for (int ring = 3; ring < 12; ring++)
        {
            for (int dy = -ring; dy <= ring; dy++)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    var origin = new GridPoint(camp.X + dx, camp.Y + dy);
                    if (game.Rules.BuildingPlacement.FootprintFree(kind, origin, inView: true, tribe))
                    {
                        return origin;
                    }
                }
            }
        }
        return null;
    }
}

public sealed class EndlessPacingTests
{
    private readonly ITestOutputHelper output;

    public EndlessPacingTests(ITestOutputHelper output) => this.output = output;

    [Theory]
    [Trait("Speed", "Long")]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    public void APeoplesTribe_SurvivesTheOpeningAgainstEightComputers(long seed)
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 8, seed: seed, newTribes: 2);
        int opening = EndlessWorlds.Content.Endless!.Pacing.OpeningGraceFrames;
        var watch = Stopwatch.StartNew();
        for (long tick = 0; game.World.Frame < opening; tick++)
        {
            game.Step(BuildingPlayer.Orders(game, tick));
            if (tick % 1500 == 0)
            {
                output.WriteLine($"frame {game.World.Frame}: people adults {TribeStanding.LivingAdults(game.World, 0)}, "
                    + $"tribes {game.World.Tribes.Count}, out {game.World.Tribes.Count(t => t.OutAtFrame >= 0)}");
            }
        }
        output.WriteLine($"{game.World.Frame} frames in {watch.Elapsed.TotalSeconds:0.0} s; people adults {TribeStanding.LivingAdults(game.World, 0)}");

        Assert.False(game.IsDefeated(0));
        Assert.True(TribeStanding.LivingAdults(game.World, 0) >= 5, "the people's tribe was nearly wiped out in the opening");
    }
}
