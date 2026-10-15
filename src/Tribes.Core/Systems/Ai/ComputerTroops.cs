using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>
/// The computer's units: the next attack is scheduled, intruders in its territory alarm its
/// adults, the army hidden near the enemy waits for its chance, the others keep their posts or
/// train and return home; waves leave and walk on at their time. Each computer tribe follows its
/// own plan and state (<see cref="ComputerPlans"/>, <see cref="TribeRoles.Ai"/>) at its own difficulty.
/// </summary>
internal sealed partial class ComputerTroops
{
    /// <summary>The attack period is given in hundreds of frames.</summary>
    private const int PeriodUnit = 100;

    /// <summary>The next attack comes a quarter period plus a random part of one and a half periods later.</summary>
    private const int PeriodQuarters = 4, PeriodRandomNumerator = 3, PeriodRandomDenominator = 2;

    /// <summary>Random mask of the attack time's draw.</summary>
    private const int AttackRoll = 0xFFFFFF;

    /// <summary>Intruders are looked for on frames where frame &amp; IntruderMask == IntruderFrame.</summary>
    private const int IntruderMask = 63, IntruderFrame = 25;

    /// <summary>Waves walk on on frames where these bits are 0.</summary>
    private const int WaveMask = 15;

    /// <summary>The mission without the computer's attacks.</summary>
    private const int NoAttacksMission = 3;

    /// <summary>A hidden attacker this close to its goal (10 * cells + facing) charges the enemy's first building.</summary>
    private const int ChargeDistance = 29;

    /// <summary>No intruder.</summary>
    public const int NoIntruder = -1;

    private readonly GameWorld world;
    private readonly ComputerPlans plans;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly TargetDistances distances;
    private readonly AttackWaves waves;
    private readonly Func<Unit, int, bool> headFor;
    private MissionAi plan = null!;
    private AiState state = null!;
    private int difficulty;

    /// <summary>
    /// Leads the computer's units in <paramref name="world"/>; <paramref name="headFor"/> makes a unit
    /// head for the nearest usable building of a kind and tells whether there is one.
    /// </summary>
    public ComputerTroops(GameWorld world, ComputerPlans plans, UnitActions actions, UnitOrders orders, TargetDistances distances,
        AttackWaves waves, Func<Unit, int, bool> headFor)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.plans = plans ?? throw new ArgumentNullException(nameof(plans));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
        this.waves = waves ?? throw new ArgumentNullException(nameof(waves));
        this.headFor = headFor ?? throw new ArgumentNullException(nameof(headFor));
    }

    /// <summary>The plan of the tribe whose turn it is.</summary>
    private MissionAi Ai => plan;

    /// <summary>
    /// The computer's units of <paramref name="tribe"/> for one frame: <paramref name="jobCount"/>
    /// posts are manned, <paramref name="rampUp"/> frames is the mission's ramp-up time.
    /// </summary>
    public void Update(int tribe, int jobCount, int rampUp)
    {
        plan = plans.For(tribe);
        state = TribeRoles.Ai(world, tribe);
        difficulty = TribeRoles.Difficulty(world, tribe);
        AiState ai = state;
        if (ai.NextWaveFrame < world.Frame)
        {
            int period = (Ai.AttackPeriodBase - (Ai.AttackPeriodHardReduction * difficulty)) * PeriodUnit;
            ai.NextWaveFrame = world.Frame + (period / PeriodQuarters)
                + world.Random.NextMasked(AttackRoll, period * PeriodRandomNumerator / PeriodRandomDenominator);
        }
        if (world.Script.ObjectiveStage >= Ai.ActivateAtStoryStep)
        {
            if ((Endless.EndlessPacing.Clock(world, tribe) & IntruderMask) == IntruderFrame && Endless.EndlessPacing.MayDefend(world, tribe))
            {
                FindIntruder(tribe);
            }
            ListView<Unit> units = world.UnitsOf(tribe);
            for (int i = 0; i < units.Count; i++)
            {
                Unit unit = units[i];
                if (unit.HitPoints > 0 && unit.Age >= UnitAges.Adult)
                {
                    Command(unit, tribe, unit.SlotInTribe, jobCount);
                }
            }
        }
        if (ai.Intruder < 0 && world.Mission != NoAttacksMission && world.Frame == ai.NextWaveFrame)
        {
            ai.WaveStartFrame = ai.NextWaveFrame;
            waves.Launch(tribe, jobCount, rampUp);
        }
        if (ai.WaveTarget >= 0 && (Endless.EndlessPacing.Clock(world, tribe) & WaveMask) == 0)
        {
            waves.Advance(tribe);
        }
    }

    /// <summary>
    /// The first adult of an enemy (in tribe order) in the computer's alarm area is the intruder (in
    /// a match only on the land the computer defends, see <c>CampLand.Defends</c>); it calls every
    /// adult of the computer not in the army to the rally.
    /// </summary>
    private void FindIntruder(int tribe)
    {
        state.Intruder = NoIntruder;
        CellRect area = Endless.EndlessPacing.DefendedLand(world, tribe, Ai.AlarmRect);
        foreach (int enemy in TribeRoles.EnemiesOf(world, tribe))
        {
            foreach (Unit unit in world.UnitsOf(enemy))
            {
                if (unit.HitPoints > 0 && unit.Age >= UnitAges.Adult && area.Contains(new GridPoint(unit.X, unit.Y))
                    && Match.CampLand.Defends(world, tribe, new GridPoint(unit.X, unit.Y)))
                {
                    state.Intruder = unit.Id;
                    AlarmAdults(tribe);
                    return;
                }
            }
        }
    }

    private void AlarmAdults(int tribe)
    {
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            if (unit.HitPoints > 0 && unit.Age >= UnitAges.Adult && (unit.Status & UnitStatus.Mustered) == 0)
            {
                unit.Status |= UnitStatus.RallyPending;
            }
        }
    }

    /// <summary>
    /// An army unit hidden close to its goal charges the enemy's first building (the player's in
    /// the campaign, the last wave's target in a match) when no wave is out; the others keep their
    /// posts. (The original then looks for an adult without a post to take over the post of a dead
    /// unit or a child, but its test looks at the dead unit itself, so no post ever changes hands;
    /// that is kept.)
    /// </summary>
    private void Command(Unit unit, int tribe, int slot, int jobCount)
    {
        if ((unit.Status & UnitStatus.Mustered) == 0)
        {
            ManagePost(unit, slot, jobCount);
            return;
        }
        int enemy = world.Mode == WorldMode.Campaign ? TribeRoles.Player(world) : state.WaveTribe;
        if (state.WaveTarget < 0 && (unit.Status & UnitStatus.Hidden) != 0 && enemy >= 0 && enemy != tribe
            && world.FirstBuildingSlot(enemy) > 0
            && distances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Measured < ChargeDistance
            && unit.OffsetX == 0 && unit.OffsetY == 0)
        {
            Building target = world.Building(world.FirstBuildingSlot(enemy));
            unit.DestinationX = target.X;
            unit.DestinationY = target.Y;
            orders.FinishOrder(unit, OrderModes.Keep);
        }
    }
}
