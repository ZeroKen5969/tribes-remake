using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// Sending a unit to the nearest building of a kind of its own tribe that can take it: standing,
/// working normally, run by an operator if the kind needs one, and with room inside.
/// </summary>
internal sealed class NearestBuildings
{
    /// <summary>The store; a site that will become a store takes deliveries of raw goods too.</summary>
    private const int Store = 6;

    /// <summary>The construction site kind whose sites may become stores.</summary>
    private const int StoreSiteKind = 16;

    /// <summary>The last delivery action a store site takes (deliveries of gathered goods).</summary>
    private const int LastSiteDelivery = 25;

    /// <summary>A squared distance larger than any on the map.</summary>
    private const int Unreachable = 1000000;

    private readonly GameWorld world;
    private readonly IReadOnlyList<BuildingKind> kinds;
    private readonly UnitActions actions;
    private readonly UnitOrders orders;
    private readonly HintSystem hints;

    /// <summary>Finds buildings in <paramref name="world"/>.</summary>
    public NearestBuildings(GameWorld world, UnitActions actions, UnitOrders orders, HintSystem hints)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.orders = orders ?? throw new ArgumentNullException(nameof(orders));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>
    /// Makes the nearest usable building of <paramref name="kind"/> (by its centre) the destination
    /// of <paramref name="unit"/> (the footprint cell nearest to the unit) and returns true; without
    /// one the unit's destination becomes its own cell. A store also counts a site becoming a store
    /// for deliveries of gathered goods, as long as no store is found. Stores take everyone.
    /// </summary>
    public bool HeadFor(Unit unit, int kind) => Find(unit, kind).Found;

    /// <summary>
    /// Sends <paramref name="unit"/> to the nearest usable building of <paramref name="kind"/> (leaving
    /// the building it is in); when there is none at all of that kind, the player hears
    /// <paramref name="missingHint"/>. Returns whether the unit was sent.
    /// </summary>
    public bool GoFor(Unit unit, int kind, int missingHint)
    {
        BuildingChoice choice = Find(unit, kind);
        if (!choice.Found)
        {
            if (!choice.KindExists)
            {
                hints.Offer(missingHint, unit.Id, unit.Id);
            }
            return false;
        }
        if (!CoupleLinks.IsOnItsCell(world, unit))
        {
            actions.SetAction(unit, ActionNumbers.LeaveBuilding);
        }
        orders.FinishOrder(unit, OrderModes.Keep);
        return true;
    }

    private BuildingChoice Find(Unit unit, int kind)
    {
        ArgumentNullException.ThrowIfNull(unit);
        bool sitesCount = kind == Store;
        bool kindExists = false;
        int best = Unreachable, chosen = 0;
        foreach (Building building in world.BuildingsOf(unit.Tribe))
        {
            int id = building.Id;
            bool storeSite = sitesCount && building.Kind == StoreSiteKind && building.Aux == Store && unit.Action <= LastSiteDelivery;
            if (building.HitPoints <= 0 || (building.Kind != kind && !storeSite))
            {
                continue;
            }
            kindExists = true;
            if (!Usable(building, kind))
            {
                continue;
            }
            int distance = SquaredDistanceToCentre(unit, building);
            if (best > distance || (sitesCount && building.Kind == kind))
            {
                sitesCount = building.Kind != kind;
                best = distance;
                chosen = id;
            }
        }
        if (chosen == 0)
        {
            unit.DestinationX = unit.X;
            unit.DestinationY = unit.Y;
            return new BuildingChoice(false, kindExists);
        }
        AimAtFootprint(unit, world.Building(chosen));
        return new BuildingChoice(true, kindExists);
    }

    /// <summary>
    /// Whether <paramref name="building"/> (looked for as <paramref name="kind"/>, whose table entries
    /// decide) can take a unit: working normally, run if the kind needs an operator, room inside.
    /// </summary>
    private bool Usable(Building building, int kind)
    {
        BuildingKind table = kinds[kind];
        bool takesEveryone = kind == Store;
        return (building.WorkMode == BuildingWorkMode.Normal || takesEveryone)
            && (table.ManagerSlot < 0 || building.OperatorOrBeds >= 1)
            && (takesEveryone || building.OccupantCount <= 0 || building.OccupantCount < table.WorkerSlots[building.Level - 1]);
    }

    private int SquaredDistanceToCentre(Unit unit, Building building)
    {
        BuildingKind kind = kinds[building.Kind];
        return GridMath.SquaredDistance(building.X + (kind.Width / 2), building.Y + (kind.Height / 2), unit.X, unit.Y);
    }

    private void AimAtFootprint(Unit unit, Building building)
    {
        BuildingKind kind = kinds[building.Kind];
        unit.DestinationX = GridMath.Clamp(unit.X, building.X, building.X + kind.Width - 1);
        unit.DestinationY = GridMath.Clamp(unit.Y, building.Y, building.Y + kind.Height - 1);
    }

    /// <summary>Whether a building was chosen, and whether the tribe has any building of the kind.</summary>
    private readonly record struct BuildingChoice(bool Found, bool KindExists);
}
