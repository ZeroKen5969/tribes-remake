using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Buildings;

/// <summary>
/// A building's turn in this frame: its kind as the frame found it, and the list entry of its
/// construction (sites) or of its upgrade or repair (busy buildings).
/// </summary>
/// <param name="Building">The building.</param>
/// <param name="Kind">Its kind at the start of its turn.</param>
/// <param name="Construction">Its construction list entry, for a site.</param>
/// <param name="Upgrade">Its upgrade or repair list entry, while busy.</param>
internal readonly record struct BuildingTurn(Building Building, int Kind, ConstructionJob? Construction, UpgradeJob? Upgrade);

/// <summary>What the occupant scan found.</summary>
/// <param name="Occupants">Units in the occupant list.</param>
/// <param name="Sleeper">In a hut: the last occupant in the first bed (0 none).</param>
/// <param name="CouplePartners">In a hut: bit 0 the man of a couple is inside, bit 1 the woman.</param>
internal readonly record struct OccupantScan(int Occupants, int Sleeper, int CouplePartners);

/// <summary>
/// The work of the units inside a building: the operator fetches what the work needs (material
/// for a site, food for the kitchen, material for an upgrade or repair), the others work or idle
/// at their positions depending on whether there is work, builders sit down while waiting for
/// material, and everyone leaves a finished site.
/// </summary>
internal sealed class OccupantWork
{
    /// <summary>The couple bit of the man (the woman's is the next one up), and both together.</summary>
    public const int ManInside = 1, BothInside = 3;

    /// <summary>Below this many meals in stock the cook fetches food.</summary>
    private const int MealsWanted = 40;

    /// <summary>
    /// The animations of a unit taking its place inside and of a builder handing over its load (as
    /// unsigned numbers); a unit playing one is given work only once it has finished.
    /// </summary>
    private const int TakingPlace = 147, HandingOver = 154;

    /// <summary>The store kind, and the site kind a store is built on.</summary>
    private const int StoreKind = 6, StoreSiteKind = 16;

    /// <summary>Carried items that are food: the cook cooks them instead of fetching more.</summary>
    private static readonly HashSet<int> FoodItems = new() { 1, 5, 6, 7 };

    private readonly GameWorld world;
    private readonly UnitActions actions;
    private readonly StoreKeeping stock;
    private readonly WorkPositionTable positions;
    private readonly WorkPositionTable busyPositions;

    /// <summary>Runs the work inside the buildings of <paramref name="world"/>.</summary>
    public OccupantWork(GameWorld world, UnitActions actions, StoreKeeping stock)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.stock = stock ?? throw new ArgumentNullException(nameof(stock));
        IReadOnlyList<BuildingKind> kinds = world.Content.Rules.Buildings.Kinds;
        positions = new WorkPositionTable(kinds, operation => operation.Slots);
        busyPositions = new WorkPositionTable(kinds, operation => operation.SlotsWhileBusy);
    }

    /// <summary>
    /// Goes through the building's occupants in list order: in a hut it notes who sleeps in the
    /// first bed and whether a couple is inside; a site that is complete sends everyone out; in
    /// other buildings each occupant that is settled and free gets its work.
    /// </summary>
    public OccupantScan ScanOccupants(BuildingTurn turn)
    {
        Building building = turn.Building;
        bool hut = turn.Kind == BuildingKindNumbers.Hut && building.WorkMode == BuildingWorkMode.Normal;
        bool siteDone = BuildingKindNumbers.IsSite(turn.Kind) && building.HitPoints >= CompletedHitPoints(building);
        int count = 0, sleeper = 0, partners = 0;
        for (int id = building.FirstOccupant; id > 0; id = world.Unit(id).NextOccupantOrCellObject)
        {
            Unit occupant = world.Unit(id);
            count++;
            if (hut)
            {
                sleeper = occupant.BuildingSlot == 0 ? id : sleeper;
                if (occupant.ActionSubState > 0 && occupant.Action == WorkActions.CoupleInHut)
                {
                    partners |= ManInside + occupant.Gender;
                }
            }
            else if (siteDone)
            {
                SendOut(occupant, building.Tribe);
            }
            else if (IsFree(occupant, building))
            {
                GiveWork(occupant, turn);
            }
        }
        return new OccupantScan(count, sleeper, partners);
    }

    /// <summary>The hit points at which site <paramref name="site"/> is finished: its building's level 1 limit.</summary>
    public int CompletedHitPoints(Building site)
    {
        ArgumentNullException.ThrowIfNull(site);
        return world.Content.Rules.Buildings.Kinds[site.Aux].MaxHitPoints[0];
    }

    /// <summary>Everyone leaves a finished site: the player's units at once, the computer's when they can.</summary>
    private void SendOut(Unit occupant, int tribe)
    {
        if (TribeRoles.IsPlayer(world, tribe))
        {
            occupant.Action = ActionNumbers.LeaveBuilding;
            occupant.OrderMode = OrderModes.Forced;
        }
        else if (TribeRoles.IsComputer(world, tribe))
        {
            occupant.OrderMode = OrderModes.Walk;
        }
    }

    /// <summary>
    /// Whether the occupant can be given work: it is not leaving nor fully inside, it is not at the
    /// door unless it has finished entering for work of its own group with empty hands, it has
    /// finished taking its place or handing over its load, and during an upgrade or repair it builds.
    /// </summary>
    private bool IsFree(Unit occupant, Building building)
    {
        if (occupant.Action == ActionNumbers.LeaveBuilding || occupant.ActionSubState == SubStates.Inside)
        {
            return false;
        }
        bool animationDone = occupant.AnimationTick >= FrameCount(occupant) - 1;
        if (occupant.ActionSubState == SubStates.AtDoor
            && !(animationDone && ServiceGroup(occupant.AssignedTask) == ServiceGroup(occupant.Action) && occupant.CarriedAmount <= 0))
        {
            return false;
        }
        int animation = occupant.AnimationNumber;
        if ((animation == TakingPlace || animation == HandingOver) && !animationDone)
        {
            return false;
        }
        return building.WorkMode == BuildingWorkMode.Normal
            || occupant.Action is WorkActions.Build or WorkActions.UpgradeWork or WorkActions.SentBuilder or WorkActions.WaitForMaterial;
    }

    /// <summary>The work of one occupant: the operator's errands, or the work at another position.</summary>
    private void GiveWork(Unit occupant, BuildingTurn turn)
    {
        if (occupant.BuildingSlot == 0)
        {
            RunErrands(occupant, turn);
        }
        else if (occupant.Action != ActionNumbers.StayInside && PositionAction(occupant, turn) is { } action)
        {
            actions.SetAction(occupant, action);
        }
    }

    /// <summary>
    /// The operator: during an upgrade or repair it fetches the material when the job needs it;
    /// at a site it fetches the next delivery while deliveries are missing; the cook fetches food
    /// (or cooks what it carries) while meals are short. At the store (or a store's site) the
    /// fetched goods are taken from the stock at once, and builders sit down or stand up.
    /// </summary>
    private void RunErrands(Unit worker, BuildingTurn turn)
    {
        if (worker.OrderMode == OrderModes.Walk)
        {
            return;
        }
        if (turn.Upgrade is { } job)
        {
            if (job.Done >= job.Needed)
            {
                job.Done = job.Needed = 0;
            }
            if (job.Needed == 0)
            {
                actions.SetAction(worker, WorkActions.FetchUpgradeMaterial);
                worker.OrderMode = OrderModes.Forced;
            }
        }
        else if (DeliveriesMissing(turn) || NeedsMeals(worker, turn))
        {
            worker.OrderMode = OrderModes.Forced;
            FetchSupplies(worker, turn);
        }
        if (IsStore(turn.Building))
        {
            KeepStore(worker, turn);
        }
    }

    private void FetchSupplies(Unit worker, BuildingTurn turn)
    {
        if (BuildingKindNumbers.IsSite(turn.Kind))
        {
            actions.SetAction(worker, Plan(turn.Building).DeliveryActions[Trips(turn)]);
        }
        else if (worker.CarriedAmount <= 0 || !FoodItems.Contains(worker.CarriedItem))
        {
            actions.SetAction(worker, WorkActions.FetchFood);
        }
        else
        {
            actions.SetAction(worker, WorkActions.Cook);
            worker.OrderMode = OrderModes.Free;
        }
    }

    /// <summary>
    /// At the store a forced errand is done on the spot: the goods leave the stock (a store's site
    /// counts a delivery) and the builder stands; when the stock is short the builder sits down to
    /// wait, unless the site has not yet reached the hit points of its deliveries so far.
    /// </summary>
    private void KeepStore(Unit worker, BuildingTurn turn)
    {
        Building building = turn.Building;
        if (worker.OrderMode != OrderModes.Forced)
        {
            Sit(worker, false);
            return;
        }
        worker.OrderMode = OrderModes.Free;
        if (stock.TakeFromStock(worker.Action, worker, building, building.Tribe))
        {
            if (building.Kind == StoreSiteKind && turn.Construction is { } site)
            {
                site.Trips++;
            }
            Sit(worker, false);
            return;
        }
        Sit(worker, building.Kind == StoreKind || building.HitPoints >= StageHitPoints(turn));
    }

    /// <summary>
    /// The action at a position other than the operator's: working while there is work, else
    /// idling. A position beyond the kind's own ones reads a neighbouring kind's (as the original
    /// does); null outside the table.
    /// </summary>
    private int? PositionAction(Unit occupant, BuildingTurn turn)
    {
        if (turn.Upgrade is { } job)
        {
            BuildingSlot? busy = busyPositions.At(turn.Kind, occupant.BuildingSlot);
            return busy == null ? null : job.Done < job.Needed ? busy.WorkAction : busy.IdleAction;
        }
        BuildingSlot? slot = positions.At(turn.Kind, occupant.BuildingSlot);
        if (slot == null)
        {
            return null;
        }
        bool work = (BuildingKindNumbers.IsSite(turn.Kind) && turn.Building.HitPoints < StageHitPoints(turn))
            || (turn.Kind == BuildingKindNumbers.Kitchen && world.Tribes[turn.Building.Tribe][TribeCounter.Meals] > 0);
        return work ? slot.WorkAction : slot.IdleAction;
    }

    /// <summary>
    /// A builder waiting for material sits down (and stays seated), or stands up again and goes
    /// back to building once it has.
    /// </summary>
    private void Sit(Unit builder, bool sit)
    {
        bool seated = builder.Animation is MaterialWaitAnimations.SitDown or MaterialWaitAnimations.Seated;
        if (sit)
        {
            actions.SetAction(builder, WorkActions.WaitForMaterial);
            if (!seated)
            {
                builder.Animation = MaterialWaitAnimations.SitDown;
                builder.AnimationTick = -1;
                builder.ActionSubState = SubStates.Alone;
            }
            else if (builder.AnimationTick == FrameCount(builder) - 1)
            {
                builder.Animation = MaterialWaitAnimations.Seated;
            }
            return;
        }
        if (seated)
        {
            builder.Animation = MaterialWaitAnimations.StandUp;
            builder.AnimationTick = -1;
        }
        else if (builder.AnimationTick == FrameCount(builder) - 1)
        {
            actions.SetAction(builder, WorkActions.Build);
        }
    }

    private bool DeliveriesMissing(BuildingTurn turn) =>
        BuildingKindNumbers.IsSite(turn.Kind) && Trips(turn) < Plan(turn.Building).DeliveryActions.Count;

    private bool NeedsMeals(Unit worker, BuildingTurn turn) =>
        turn.Kind == BuildingKindNumbers.Kitchen && world.Tribes[turn.Building.Tribe][TribeCounter.Meals] < MealsWanted
        && worker.Action == WorkActions.KitchenIdle;

    /// <summary>The hit points a site may reach with the deliveries made so far.</summary>
    private int StageHitPoints(BuildingTurn turn) => Plan(turn.Building).ProgressHitPoints[Trips(turn)];

    private static int Trips(BuildingTurn turn) => turn.Construction?.Trips ?? 0;

    private ConstructionPlan Plan(Building site) => world.Content.Rules.Buildings.Kinds[site.Aux].Construction!;

    private static bool IsStore(Building building) =>
        building.Kind == StoreKind || (building.Kind == StoreSiteKind && building.Aux == StoreKind);

    private int FrameCount(Unit unit) => world.Content.Rules.Animations.Animations[unit.AnimationNumber].FrameCount;

    private int ServiceGroup(int action)
    {
        ActionTables tables = world.Content.Rules.Actions;
        return tables.Categories[tables.Actions[action].Category].ServiceGroup;
    }
}
