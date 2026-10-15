using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Combat;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.Endless;

public sealed class AllianceTests
{
    /// <summary>Puts a finished alliance building of <paramref name="tribe"/> near its camp; returns its slot.</summary>
    internal static int BuildHall(GameSimulation game, int tribe)
    {
        GridPoint camp = game.World.Tribes[tribe].Camp;
        for (int ring = 6; ring < 30; ring++)
        {
            for (int dy = -ring; dy <= ring; dy += 3)
            {
                for (int dx = -ring; dx <= ring; dx += 3)
                {
                    var cell = new GridPoint(camp.X + dx, camp.Y + dy);
                    PlacementResult result = game.Rules.BuildingPlacement.Place(
                        new PlacementRequest(tribe, AllianceBuilding.Kind, cell, 1, Immediate: true));
                    if (result.Outcome == PlacementOutcome.Placed)
                    {
                        return result.Slot;
                    }
                }
            }
        }
        throw new InvalidOperationException("no room for an alliance building");
    }

    private static TickResult Order(GameSimulation game, GameOrder order) => game.Step(TickInput.WithOrders(order));

    private static GameSimulation TwoPeople(out int a, out int b)
    {
        GameSimulation game = EndlessWorlds.Start(humans: 2, computers: 1, seed: 33);
        (a, b) = (0, 1);
        return game;
    }

    [Fact]
    public void Invite_NeedsAFinishedAllianceBuildingAndAPlayingPerson()
    {
        GameSimulation game = TwoPeople(out int a, out int b);

        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new InviteAlly(a, b)).Outcomes[0]);
        BuildHall(game, a);
        Assert.True(AllianceRules.HasAllianceBuilding(game.World, a));
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new InviteAlly(a, 2)).Outcomes[0]);
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new InviteAlly(a, a)).Outcomes[0]);
        game.SetController(b, TribeController.Absent(1));
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new InviteAlly(a, b)).Outcomes[0]);
        game.SetController(b, TribeController.Human(1));

        TickResult invited = Order(game, new InviteAlly(a, b));

        Assert.Equal(OrderOutcome.Applied, invited.Outcomes[0]);
        Assert.Contains(new AllianceInvited(a, b), invited.Events);
        Assert.Single(game.World.Endless!.Invitations);
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new InviteAlly(a, b)).Outcomes[0]);
    }

    [Fact]
    public void Accept_MakesAlliesAndLeaveEndsIt()
    {
        GameSimulation game = TwoPeople(out int a, out int b);
        BuildHall(game, a);
        Order(game, new InviteAlly(a, b));

        TickResult accepted = Order(game, new AnswerAlliance(b, a, Accept: true));

        Assert.Equal(OrderOutcome.Applied, accepted.Outcomes[0]);
        Assert.Contains(new AllianceFormed(a, b), accepted.Events);
        Assert.True(game.World.Relations.AreAllies(a, b));
        Assert.Empty(game.World.Endless!.Invitations);
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new AnswerAlliance(b, a, true)).Outcomes[0]);
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new InviteAlly(a, b)).Outcomes[0]);

        TickResult left = Order(game, new LeaveAlliance(b, a));

        Assert.Equal(OrderOutcome.Applied, left.Outcomes[0]);
        Assert.Contains(new AllianceEnded(a, b, b), left.Events);
        Assert.True(game.World.Relations.AreEnemies(a, b));
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new LeaveAlliance(b, a)).Outcomes[0]);
    }

    [Fact]
    public void Alliance_EndsWhenOneSideIsOut()
    {
        GameSimulation game = TwoPeople(out int a, out int b);
        BuildHall(game, a);
        Order(game, new InviteAlly(a, b));
        Order(game, new AnswerAlliance(b, a, true));
        Assert.True(game.World.Relations.AreAllies(a, b));

        game.SetController(b, TribeController.Forfeited(1));
        TickResult after = game.Step();

        Assert.Contains(new AllianceEnded(a, b, b), after.Events);
        Assert.False(game.World.Relations.AreAllies(a, b));
        Assert.Empty(game.World.Relations.Alliances);
        Assert.False(AllianceRules.CanTransfer(game.World, a, b));
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new InviteAlly(a, b)).Outcomes[0]);
        Assert.DoesNotContain(game.Step().Events, e => e is AllianceEnded);
    }

    [Fact]
    public void Alliance_EndsWhenAnAllyIsWipedOut()
    {
        GameSimulation game = TwoPeople(out int a, out int b);
        BuildHall(game, a);
        Order(game, new InviteAlly(a, b));
        Order(game, new AnswerAlliance(b, a, true));
        GameWorld world = game.World;
        foreach (Unit unit in world.UnitsOf(b))
        {
            unit.HitPoints = 0;
        }
        foreach (Building building in world.BuildingsOf(b))
        {
            building.HitPoints = 0;
        }

        var events = new List<GameEvent>();
        for (int tick = 0; tick < 3; tick++)
        {
            events.AddRange(game.Step().Events);
        }

        Assert.True(world.Tribes[b].OutAtFrame >= 0);
        Assert.Contains(new AllianceEnded(a, b, b), events);
        Assert.True(world.Relations.AreEnemies(a, b));
    }

    [Fact]
    public void Decline_AndLapse_CloseTheInvitation()
    {
        GameSimulation game = TwoPeople(out int a, out int b);
        BuildHall(game, a);
        Order(game, new InviteAlly(a, b));

        TickResult declined = Order(game, new AnswerAlliance(b, a, Accept: false));

        Assert.Contains(new AllianceDeclined(a, b), declined.Events);
        Assert.False(game.World.Relations.AreAllies(a, b));
        Order(game, new InviteAlly(a, b));
        var events = new List<GameEvent>();
        int frames = EndlessWorlds.Content.Endless!.Alliance.InvitationFrames;
        for (int tick = 0; tick <= frames; tick++)
        {
            events.AddRange(game.Step().Events);
        }
        Assert.Contains(new AllianceInvitationLapsed(a, b), events);
        Assert.Empty(game.World.Endless!.Invitations);
    }

    [Fact]
    public void Transfer_NeedsAlliesWithBuildingsAndKeepsToTheCap()
    {
        GameSimulation game = TwoPeople(out int a, out int b);
        BuildHall(game, a);
        Order(game, new InviteAlly(a, b));
        Order(game, new AnswerAlliance(b, a, true));
        game.World.Tribes[a][TribeCounter.Wood] = 300;
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new TransferGoods(a, b, TribeCounter.Wood, 10)).Outcomes[0]);
        BuildHall(game, b);
        int cap = EndlessWorlds.Content.Endless!.Alliance.GoodsCap;
        game.World.Tribes[b][TribeCounter.Wood] = cap - 50;

        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new TransferGoods(a, b, TribeCounter.Wood, 301)).Outcomes[0]);
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new TransferGoods(a, b, TribeCounter.Wood, 0)).Outcomes[0]);
        Assert.Equal(OrderOutcome.NotAvailable, Order(game, new TransferGoods(a, b, TribeCounter.Population, 1)).Outcomes[0]);
        int woodBefore = game.World.Tribes[a][TribeCounter.Wood];
        TickResult given = Order(game, new TransferGoods(a, b, TribeCounter.Wood, 100));

        Assert.Equal(OrderOutcome.Applied, given.Outcomes[0]);
        GoodsTransferred moved = given.Events.OfType<GoodsTransferred>().Single();
        Assert.Equal(50, moved.Amount);
        Assert.Equal(cap, game.World.Tribes[b][TribeCounter.Wood]);
        Assert.Equal(woodBefore - 50, game.World.Tribes[a][TribeCounter.Wood]);
    }

    [Fact]
    public void Allies_StopHurtingEachOther()
    {
        GameSimulation game = TwoPeople(out int a, out int b);
        BuildHall(game, a);
        GridPoint target = game.World.Tribes[b].Camp;
        int[] attackers = game.World.UnitsOf(a).Select(u => u.Id)
            .Where(id => game.World.Unit(id).InUse && game.World.Unit(id).Age >= 3).Take(8).ToArray();
        Order(game, new MoveUnits(a, new UnitSet(attackers), target, 0, Hurry: true));
        for (int tick = 0; tick < 400; tick++)
        {
            game.Step();
        }
        Order(game, new InviteAlly(a, b));
        Order(game, new AnswerAlliance(b, a, true));

        var blows = new List<BlowLanded>();
        for (int tick = 0; tick < 1500; tick++)
        {
            blows.AddRange(game.Step().Events.OfType<BlowLanded>());
        }

        Assert.DoesNotContain(blows, blow => IsBetween(game.World, blow, a, b));
    }

    [Fact]
    public void AllianceBuilding_IsOfferedAndBuiltOnlyInEndlessWorlds()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 0, seed: 4);
        GameSimulation campaign = GameSimulation.StartMission(EndlessWorlds.Content, 8, 0);

        Assert.Contains(AllianceBuilding.Kind, OrderMenus.BuildableKinds(game.World));
        Assert.DoesNotContain(AllianceBuilding.Kind, OrderMenus.BuildableKinds(campaign.World));
        Assert.Equal(OrderOutcome.NotOffered,
            campaign.Step(TickInput.WithOrders(new PlaceBuilding(0, AllianceBuilding.Kind, new GridPoint(10, 10)))).Outcomes[0]);
    }

    [Fact]
    public void Codec_CarriesTheAllianceOrders()
    {
        GameOrder[] orders =
        {
            new InviteAlly(3, 7), new AnswerAlliance(3, 7, true), new AnswerAlliance(3, 200, false), new LeaveAlliance(3, 7),
            new TransferGoods(3, 7, TribeCounter.Spears, 123),
        };
        foreach (GameOrder order in orders)
        {
            Assert.True(GameOrderCodec.TryDecode(GameOrderCodec.Encode(order), 3, out GameOrder? decoded));
            Assert.Equal(order, decoded);
        }
        byte[] bad = GameOrderCodec.Encode(new TransferGoods(3, 7, TribeCounter.Wood, 1));
        bad[3] = 200;
        Assert.False(GameOrderCodec.TryDecode(bad, 3, out _));
        Assert.False(GameOrderCodec.TryDecode(new byte[] { GameOrderCodec.FormatVersion, 21, 1, 5 }, 3, out _));
    }

    private static bool IsBetween(GameWorld world, BlowLanded blow, int a, int b)
    {
        if (blow.StrikerTribe < 0 || blow.Category is not (CellCategories.Unit or CellCategories.Building))
        {
            return false;
        }
        int victim = blow.Category == CellCategories.Unit ? world.TribeOfUnit(blow.Slot) : world.TribeOfBuilding(blow.Slot);
        return (blow.StrikerTribe == a && victim == b) || (blow.StrikerTribe == b && victim == a);
    }
}

public sealed class AllianceConstructionTests
{
    [Fact]
    public void AllianceBuilding_IsBuiltFromItsSiteByTheTribe()
    {
        GameSimulation game = EndlessWorlds.Start(humans: 1, computers: 0, seed: 12);
        GameWorld world = game.World;
        world.Tribes[0][TribeCounter.Wood] = 200;
        world.Tribes[0][TribeCounter.Stones] = 200;
        GridPoint camp = world.Tribes[0].Camp;
        OrderOutcome placed = OrderOutcome.Blocked;
        var tried = new List<OrderOutcome>();
        for (int dy = -10; dy <= 10 && placed != OrderOutcome.Applied; dy++)
        {
            for (int dx = -10; dx <= 10 && placed != OrderOutcome.Applied; dx++)
            {
                var origin = new GridPoint(camp.X + dx, camp.Y + dy);
                if (game.Rules.BuildingPlacement.FootprintFree(AllianceBuilding.Kind, origin, inView: true, 0))
                {
                    placed = game.Step(TickInput.WithOrders(new PlaceBuilding(0, AllianceBuilding.Kind, origin))).Outcomes[0];
                    tried.Add(placed);
                }
            }
        }
        Assert.NotEmpty(tried);
        Assert.Equal(OrderOutcome.Applied, placed);
        Assert.Equal(OrderOutcome.NotOffered,
            game.Step(TickInput.WithOrders(new PlaceBuilding(0, AllianceBuilding.Kind, new GridPoint(camp.X - 20, camp.Y)))).Outcomes[0]);

        int site = Enumerable.Range(1, world.BuildingSlots).First(id => world.Building(id).InUse && world.Building(id).Aux == AllianceBuilding.Kind);
        Building planned = world.Building(site);
        int[] builders = world.UnitsOf(0).Select(u => u.Id)
            .Where(id => world.Unit(id).InUse && world.Unit(id).Age >= 3).Take(6).ToArray();
        game.Step(TickInput.WithOrders(new MoveUnits(0, new UnitSet(builders), new GridPoint(planned.X, planned.Y),
            Maps.CellCode.OfBuilding(site), Hurry: false)));
        for (int tick = 0; tick < 12000 && !AllianceRules.HasAllianceBuilding(world, 0); tick++)
        {
            game.Step();
        }

        Assert.True(AllianceRules.HasAllianceBuilding(world, 0), "the alliance building was not finished");
        Building hall = world.Building(AllianceBuilding.SlotOf(world, 0));
        Assert.Equal(world.Content.Rules.Buildings.Kinds[AllianceBuilding.Kind].MaxHitPoints[0], hall.MaxHitPoints);
    }
}

public sealed class DevAllianceHallTests
{
    [Fact]
    public void DevAllianceHalls_GiveThePeopleAFinishedHallAndStock()
    {
        GameSimulation game = GameSimulation.StartEndless(EndlessWorlds.Content,
            EndlessWorlds.Setup(humans: 2, computers: 1) with { DevAllianceHalls = true });

        Assert.True(AllianceRules.HasAllianceBuilding(game.World, 0));
        Assert.True(AllianceRules.HasAllianceBuilding(game.World, 1));
        Assert.False(AllianceRules.HasAllianceBuilding(game.World, 2));
        Assert.True(game.World.Tribes[0][TribeCounter.Wood] >= EndlessStart.DevStock);
        Assert.True(AllianceRules.CanInvite(game.World, 0, 1));
    }
}
