using System;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>
/// A computer tribe's turn, once a frame before its units move (and not while a story scene of
/// the mission plays): its building plan, its units and waves, armoury orders and spells, by the
/// plan it follows (<see cref="ComputerPlans"/>).
/// </summary>
internal sealed class ComputerPlayer
{
    /// <summary>The ramp-up time is given in units of this many frames.</summary>
    public const int RampUpUnit = 500;

    private readonly GameWorld world;
    private readonly ComputerPlans plans;
    private readonly ComputerBuilder builder;
    private readonly ComputerTroops troops;
    private readonly ComputerOrders orders;

    /// <summary>Plays the computer tribes of <paramref name="world"/>.</summary>
    public ComputerPlayer(GameWorld world, ComputerPlans plans, ComputerBuilder builder, ComputerTroops troops, ComputerOrders orders)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.plans = plans ?? throw new ArgumentNullException(nameof(plans));
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
        this.troops = troops ?? throw new ArgumentNullException(nameof(troops));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
    }

    /// <summary>The turn of the computer's <paramref name="tribe"/>.</summary>
    public void Step(int tribe)
    {
        if (world.Script.ObjectiveStage >= StoryProgress.ScenePlaying)
        {
            return;
        }
        MissionAi ai = plans.For(tribe);
        int jobCount = ai.Posts.Count;
        int rampUp = ai.RampUpUnits * RampUpUnit;
        if (world.Tribes[tribe][TribeCounter.Population] > 0 && TribeRoles.Ai(world, tribe).Intruder < 1)
        {
            builder.CheckBuildPlan(tribe);
        }
        troops.Update(tribe, jobCount, rampUp);
        if (orders.IsOrderFrame(tribe, ai.ArmouryOrderInterval))
        {
            orders.OrderFromArmouries(tribe, rampUp);
        }
        if (orders.IsOrderFrame(tribe, ai.SpellInterval))
        {
            orders.CastSpells(tribe, rampUp);
        }
    }
}
