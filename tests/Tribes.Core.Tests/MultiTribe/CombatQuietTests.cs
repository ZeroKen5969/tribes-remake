using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;
using Xunit;

namespace Tribes.Core.Tests.MultiTribe;

/// <summary>
/// In a match each tribe has its own quiet time: a fight between two tribes somewhere else does not
/// keep a third tribe from naming a new chief, from its villagers taking up work of their own
/// choice, or from its hints; the tribes that fight (attacker and attacked) are not quiet. The
/// campaign keeps the original's one world-wide quiet time.
/// </summary>
public sealed class CombatQuietTests
{
    private const int QuietFrames = 50;

    /// <summary>A hint that waits for quiet (not one about something out of sight, not a compulsory one).</summary>
    private const int OrdinaryHint = 1;

    /// <summary>A human (tribe 0) and two computer tribes; tribes 1 and 2 fight each other every frame from <see cref="Fight"/>.</summary>
    private static GameSimulation ThreeTribes() =>
        GameSimulation.StartMatch(MatchWorlds.Content, MatchWorlds.Setup(1, 21, MatchWorlds.Controllers(1, 2)));

    [Fact]
    public void AFightElsewhere_DoesNotDelayANewChief()
    {
        GameSimulation game = ThreeTribes();
        GameWorld world = game.World;
        Unit chief = world.Unit(world.Tribes[0][TribeCounter.Chief]);
        game.Rules.UnitLifecycle.Kill(chief);

        bool asked = false;
        for (int tick = 0; tick < 3 * QuietFrames && !asked; tick++)
        {
            Fight(game);
            asked = world.Tribes[0].AwaitingNewChief;
            Assert.True(world.FramesSinceCombat < QuietFrames, "tribes 1 and 2 keep fighting");
        }

        Assert.True(asked, "tribe 0 is asked for a new chief while tribes 1 and 2 fight");
    }

    [Fact]
    public void AFightElsewhere_DoesNotStopVillagersTakingUpTheirOwnWork()
    {
        GameSimulation game = ThreeTribes();
        GameWorld world = game.World;
        for (int tick = 0; tick < 2 * QuietFrames; tick++)
        {
            Fight(game);
        }
        Unit villager = world.UnitsOf(0).First(u => u.HitPoints > 0 && u.Age >= UnitAges.Adult);
        (villager.DestinationX, villager.DestinationY) = (villager.X + 2, villager.Y + 1);

        game.Rules.UnitOrders.TakeOrder(villager, new OrderChoice(-1, 0, ActionNumbers.Idle, default), FinishModes.OwnChoice);

        Assert.True(world.FramesSinceCombat < QuietFrames);
        Assert.True(TribeRoles.FramesSinceCombat(world, 0) >= QuietFrames);
        Assert.Equal((villager.DestinationX, villager.DestinationY), (villager.TaskSiteX, villager.TaskSiteY));
    }

    [Fact]
    public void TheTribesThatFight_AreNotQuiet_TheOthersAre()
    {
        GameSimulation game = ThreeTribes();
        GameWorld world = game.World;
        for (int tick = 0; tick < 2 * QuietFrames; tick++)
        {
            Fight(game);
        }

        Assert.True(TribeRoles.FramesSinceCombat(world, 1) < QuietFrames, "the attacker");
        Assert.True(TribeRoles.FramesSinceCombat(world, 2) < QuietFrames, "the attacked");
        Assert.True(TribeRoles.FramesSinceCombat(world, 0) >= QuietFrames, "the tribe far away");
    }

    [Fact]
    public void AFightElsewhere_DoesNotHoldBackHints()
    {
        GameSimulation game = ThreeTribes();
        GameWorld world = game.World;
        for (int tick = 0; tick < 2 * QuietFrames; tick++)
        {
            Fight(game);
        }
        game.Rules.TakeEvents();

        game.Rules.Hints.Offer(0, OrdinaryHint, 0, 0);

        Assert.Contains(game.Rules.TakeEvents(), e => e is HintEvent { Tribe: 0 });
        Assert.True(world.FramesSinceCombat < QuietFrames);
    }

    [Fact]
    public void Campaign_KeepsOneWorldWideQuietTime()
    {
        GameSimulation game = SlotGrowthTests.Campaign();
        GameWorld world = game.World;
        world.FramesSinceCombat = 0;

        Assert.Equal(0, TribeRoles.FramesSinceCombat(world, 0));
        Assert.Equal(0, TribeRoles.FramesSinceCombat(world, 1));
        world.FramesSinceCombat = QuietFrames;
        Assert.Equal(QuietFrames, TribeRoles.FramesSinceCombat(world, 0));
    }

    [Fact]
    public void QuietTimes_AreSaved()
    {
        GameSimulation game = ThreeTribes();
        for (int tick = 0; tick < 10; tick++)
        {
            Fight(game);
        }
        GameSimulation loaded = GameSimulation.Load(MatchWorlds.Content, game.Save());

        Assert.Equal(game.StateHash(), loaded.StateHash());
        Assert.Equal(game.World.Tribes.Select(t => t.FramesSinceCombat), loaded.World.Tribes.Select(t => t.FramesSinceCombat));
        Assert.NotEqual(game.World.Tribes[0].FramesSinceCombat, game.World.Tribes[1].FramesSinceCombat);
    }

    /// <summary>One frame in which an adult of tribe 1 attacks an adult of tribe 2 (wherever they are).</summary>
    private static void Fight(GameSimulation game)
    {
        GameWorld world = game.World;
        Unit attacker = world.UnitsOf(1).First(u => u.HitPoints > 0 && u.Age >= UnitAges.Adult);
        Unit victim = world.UnitsOf(2).First(u => u.HitPoints > 0 && u.Age >= UnitAges.Adult);
        (attacker.Action, attacker.TargetIndex, attacker.TargetCellCode) = (0, victim.Id, CellCode.OfUnit(victim.Id));
        game.Step();
    }
}
