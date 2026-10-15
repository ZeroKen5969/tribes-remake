using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Ai;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Match;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>The per-tribe rules of a match: chiefs, hints and notices, fog, the computer's plan and targets, going out.</summary>
public sealed class MatchSystemsTests
{
    private static GameSimulation NewMatch(int humans, int computers, int map = 0, long seed = 2, int[]? teams = null) =>
        GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(map, seed, MatchWorlds.Controllers(humans, computers), teams));

    [Fact]
    public void LostChief_IsAskedForPerTribe_AndNamedByThatPlayer()
    {
        GameSimulation game = NewMatch(2, 0);
        GameWorld world = game.World;
        world.FramesSinceCombat = 50;
        Unit chief = world.Unit(world.Tribes[1][TribeCounter.Chief]);
        chief.HitPoints = 0;
        HintEvent[] hints = game.Step().Events.OfType<HintEvent>().ToArray();
        Assert.Contains(hints, h => h.Hint == StoryTriggers.NewChiefHint && h.Tribe == 1);
        Assert.True(TribeRoles.AwaitsNewChief(world, 1));
        Assert.False(TribeRoles.AwaitsNewChief(world, 0));
        int next = MatchWorlds.LiveAdults(world, 1).First();
        Assert.Equal(OrderOutcome.NotAvailable, game.Step(TickInput.WithOrders(new AppointChief(0, MatchWorlds.LiveAdults(world, 0).First()))).Outcomes[0]);
        Assert.Equal(OrderOutcome.Applied, game.Step(TickInput.WithOrders(new AppointChief(1, next))).Outcomes[0]);
        Assert.Equal(next, world.Tribes[1][TribeCounter.Chief]);
        Assert.False(TribeRoles.AwaitsNewChief(world, 1));
    }

    [Fact]
    public void HintsAndNotices_GoToTheirTribesPlayer_NotToTheComputer()
    {
        GameSimulation game = NewMatch(2, 1);
        GameWorld world = game.World;
        world.Frame = 5;
        world.FramesSinceCombat = 50;
        HintSystem hints = game.Rules.Hints;
        game.Rules.TakeEvents();
        int unitOfTribe1 = MatchWorlds.LiveAdults(world, 1).First();
        hints.Offer(12, unitOfTribe1, unitOfTribe1);
        hints.Offer(12, MatchWorlds.LiveAdults(world, 2).First(), 0);
        hints.Notice(2, 6, 1, 1, 0);
        hints.Notice(0, 6, 1, 1, 0);
        hints.Offer(1, 12, MatchWorlds.LiveAdults(world, 0).First(), 0);
        var events = game.Rules.TakeEvents();
        HintEvent hint = Assert.Single(events.OfType<HintEvent>());
        Assert.Equal((1, unitOfTribe1), (hint.Tribe, hint.Subject));
        Assert.True(world.Tribes[1].Hints.Cooldown(world.Content.Rules.Story.Hints[12].Channel) >= 0);
        Assert.Equal(HintState.Free, world.Tribes[0].Hints.Cooldown(world.Content.Rules.Story.Hints[12].Channel));
        Assert.Equal(0, Assert.Single(events.OfType<NoticeEvent>()).Tribe);
        world.Tribes[0].Hints.Enabled = false;
        hints.UpdateCooldowns();
        Assert.True(world.Tribes[0].Hints.Cooldown(0) > 0);
    }

    [Fact]
    public void EveryHumanTribe_HasItsOwnFog_TheComputerNone()
    {
        GameSimulation game = NewMatch(2, 1);
        for (int tick = 0; tick < 40; tick++)
        {
            game.Step();
        }
        GameWorld world = game.World;
        Assert.True(world.Fog(0).IsVisible(world.Tribes[0].Camp));
        Assert.False(world.Fog(0).IsExplored(world.Tribes[1].Camp));
        Assert.True(world.Fog(1).IsVisible(world.Tribes[1].Camp));
        Assert.False(world.Fog(2).IsExplored(world.Tribes[2].Camp));
        Assert.Equal(FogState.Visible, game.Rules.Passability.FogAt(world.Tribes[1].Camp, 1));
        Assert.Equal(FogState.Unexplored, game.Rules.Passability.FogAt(world.Tribes[1].Camp, 0));
    }

    [Fact]
    public void ComputerPlan_IsTheMissionsMovedToTheCamp()
    {
        GameSimulation game = NewMatch(1, 2, map: 1);
        GameWorld world = game.World;
        ComputerPlans plans = game.Rules.ComputerPlans;
        MissionAi template = plans.Template;
        MissionAi plan = plans.For(2);
        Assert.Same(plan, plans.For(2));
        Assert.Equal(template.BuildPlan.Count, plan.BuildPlan.Count);
        Assert.Equal(template.Posts.Count, plan.Posts.Count);
        GridPoint camp = world.Tribes[2].Camp;
        Assert.True(System.Math.Abs(plan.Home.X - camp.X) <= 3 && System.Math.Abs(plan.Home.Y - camp.Y) <= 3);
        int dx = camp.X - template.Home.X, dy = camp.Y - template.Home.Y;
        Assert.Equal(System.Math.Clamp(template.TerritoryRect.Left + dx, 1, world.Map.PlayableSize.Width - 2), plan.TerritoryRect.Left);
        Assert.Equal(System.Math.Clamp(template.TerritoryRect.Top + dy, 1, world.Map.PlayableSize.Height - 2), plan.TerritoryRect.Top);
        Assert.All(plan.BuildPlan, e => Assert.True(world.Map.InPlay(e.Cell)));
        GameSimulation campaign = GameSimulation.StartMission(MatchWorlds.Content, 8, 0);
        Assert.Same(campaign.Rules.ComputerPlans.Template, campaign.Rules.ComputerPlans.For(1));
    }

    [Fact]
    public void ComputerTarget_IsTheNearestEnemy_OrAWeakerOneNearlyAsNear_NeverAnAlly()
    {
        GameSimulation game = NewMatch(0, 4, map: 0, seed: 3, teams: [0, 0, 1, 2]);
        GameWorld world = game.World;
        ComputerTargets targets = game.Rules.ComputerTargets;
        int choice = targets.Choose(0);
        Assert.NotEqual(1, choice);
        Assert.Contains(choice, new[] { 2, 3 });
        int other = choice == 2 ? 3 : 2;
        foreach (int adult in MatchWorlds.LiveAdults(world, other).Skip(1).ToArray())
        {
            world.Unit(adult).HitPoints = 0;
        }
        long Distance(int tribe)
        {
            GridPoint home = game.Rules.ComputerPlans.For(0).Home, seat = TribeStanding.Seat(world, tribe);
            return ((long)(home.X - seat.X) * (home.X - seat.X)) + ((long)(home.Y - seat.Y) * (home.Y - seat.Y));
        }
        bool nearEnough = Distance(other) * 4 <= System.Math.Min(Distance(2), Distance(3)) * 9;
        Assert.Equal(nearEnough ? other : choice, targets.Choose(0));
        world.SetTeam(2, 0);
        world.SetTeam(3, 0);
        Assert.Equal(-1, targets.Choose(0));
        Assert.DoesNotContain(targets.Route(0, 1, 0), p => !world.Map.InPlay(p));
    }

    [Fact]
    public void ATribe_IsOutWithoutAdultsAndBuildings_ChildrenAloneDoNotCount()
    {
        GameSimulation game = NewMatch(1, 2);
        GameWorld world = game.World;
        int tribe = 2;
        foreach (int adult in MatchWorlds.LiveAdults(world, tribe).ToArray())
        {
            world.Unit(adult).Age = 5;
        }
        Assert.False(TribeStanding.IsOut(world, tribe));
        foreach (Building building in world.BuildingsOf(tribe))
        {
            building.HitPoints = 0;
        }
        Assert.True(TribeStanding.IsOut(world, tribe));
        Assert.Equal(world.Tribes[tribe].Camp, TribeStanding.Seat(world, tribe));
        TickResult result = game.Step();
        Assert.Contains(result.Events, e => e is TribeOut { Tribe: 2, Forfeited: false });
        Assert.Equal(TribeOutcome.Defeated, game.Match.Tribes[tribe].Outcome);
        Assert.True(game.IsDefeated(tribe));
    }
}
