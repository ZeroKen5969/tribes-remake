using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Vision;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Entities;

/// <summary>
/// A building, construction site or trap to place. <paramref name="Immediate"/> is false for a
/// player's order: the footprint must then be in view, and a trap is only ordered from an armoury.
/// </summary>
/// <param name="Tribe">The owning tribe.</param>
/// <param name="Kind">The building kind (sites and traps included).</param>
/// <param name="Cell">The footprint's top-left cell.</param>
/// <param name="Level">The level 1..3, or 0 for the mission's default level of the kind (kinds 0..11).</param>
/// <param name="Immediate">Whether the game places it at once (story, computer, finished sites).</param>
internal readonly record struct PlacementRequest(int Tribe, int Kind, GridPoint Cell, int Level, bool Immediate)
{
    /// <summary>The <see cref="Slot"/> value asking for a new slot.</summary>
    public const int NewSlot = -1;

    /// <summary>The level of a building the player orders.</summary>
    public const int OrderedLevel = 1;

    /// <summary>The slot to use (a site becoming its building keeps its slot), or <see cref="NewSlot"/>.</summary>
    public int Slot { get; init; } = NewSlot;

    /// <summary>For a construction site: the building kind it becomes.</summary>
    public int SiteTarget { get; init; }

    /// <summary>For a trap: the building that ordered it.</summary>
    public int TrapOwner { get; init; }

    /// <summary>For a trap the player orders: the armoury queue that makes it.</summary>
    public int TrapQueue { get; init; }
}

/// <summary>How a placement ended.</summary>
internal enum PlacementOutcome
{
    /// <summary>It was placed.</summary>
    Placed,

    /// <summary>The tribe has no free building slot (or no room on a list it needs).</summary>
    NoSlot,

    /// <summary>The footprint is not free (or, for an order, not in view).</summary>
    Blocked,
}

/// <summary>The outcome of a placement and the slot used (a building slot, or a trap slot for traps).</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Slot">The slot, when placed.</param>
internal readonly record struct PlacementResult(PlacementOutcome Outcome, int Slot)
{
    /// <summary>The placement failed for lack of a slot.</summary>
    public static PlacementResult NoSlot => new(PlacementOutcome.NoSlot, 0);

    /// <summary>The placement failed because the footprint was taken.</summary>
    public static PlacementResult Blocked => new(PlacementOutcome.Blocked, 0);
}

/// <summary>Putting buildings, construction sites and traps on the map.</summary>
internal sealed class BuildingPlacement
{
    /// <summary>Living space a hut gives.</summary>
    public const int HutLivingSpace = 4;

    /// <summary>Most living space a tribe can have in the campaign (its 50 unit slots; a match has no limit).</summary>
    public const int MaxLivingSpace = 50;

    /// <summary>The level a construction site is kept at.</summary>
    public const int SiteLevel = 3;

    /// <summary>The armoury product of the first trap kind (the others follow).</summary>
    public const int FirstTrapProduct = 6;

    private const int DefaultLevelWithoutTable = 1;

    private readonly GameWorld world;
    private readonly IReadOnlyList<BuildingKind> kinds;
    private readonly BuildingWorkLists workLists;
    private readonly CoupleLinks couples;
    private readonly Passability passability;
    private readonly SightSystem sight;
    private readonly HintSystem hints;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public BuildingPlacement(GameWorld world, BuildingWorkLists workLists, CoupleLinks couples, Passability passability,
        SightSystem sight, HintSystem hints)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.workLists = workLists ?? throw new ArgumentNullException(nameof(workLists));
        this.couples = couples ?? throw new ArgumentNullException(nameof(couples));
        this.passability = passability ?? throw new ArgumentNullException(nameof(passability));
        this.sight = sight ?? throw new ArgumentNullException(nameof(sight));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>
    /// Places what <paramref name="request"/> asks for. Even a trap or a reused slot needs the tribe
    /// to have a free building slot, as in the original (in a match a tribe always has room).
    /// </summary>
    public PlacementResult Place(PlacementRequest request)
    {
        if (!world.HasFreeBuildingSlot(request.Tribe))
        {
            ReportNoSlot(request.Tribe);
            return PlacementResult.NoSlot;
        }
        if (request.Slot == PlacementRequest.NewSlot
            && !FootprintFree(request.Kind, request.Cell, inView: !request.Immediate, request.Tribe))
        {
            return PlacementResult.Blocked;
        }
        if (BuildingKindNumbers.IsTrap(request.Kind))
        {
            return PlaceTrap(request);
        }
        int slot = request.Slot != PlacementRequest.NewSlot ? request.Slot : world.FreeBuildingSlot(request.Tribe)!.Id;
        return PlaceBuilding(request, slot);
    }

    /// <summary>
    /// Whether every cell of a <paramref name="kind"/> footprint at <paramref name="origin"/> is in
    /// play and empty, and with <paramref name="inView"/> seen now by the original's player.
    /// </summary>
    public bool FootprintFree(int kind, GridPoint origin, bool inView) =>
        FootprintFree(kind, origin, inView, TribeRoles.Player(world));

    /// <summary>
    /// Whether every cell of a <paramref name="kind"/> footprint at <paramref name="origin"/> is in
    /// play and empty, and with <paramref name="inView"/> seen now by <paramref name="tribe"/> (a
    /// computer tribe sees everything; in the campaign the player's view counts, as in the original).
    /// </summary>
    public bool FootprintFree(int kind, GridPoint origin, bool inView, int tribe)
    {
        int viewer = world.Mode == WorldMode.Campaign ? TribeRoles.Player(world) : tribe;
        inView &= TribeRoles.IsPlayer(world, viewer);
        BuildingKind definition = kinds[kind];
        for (int dy = 0; dy < definition.Height; dy++)
        {
            for (int dx = 0; dx < definition.Width; dx++)
            {
                var cell = new GridPoint(origin.X + dx, origin.Y + dy);
                if (!world.Map.InPlay(cell) || !world.Map.CellAt(cell).IsEmpty
                    || (inView && passability.FogAt(cell, viewer) != FogState.Visible))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private void ReportNoSlot(int tribe)
    {
        if (TribeRoles.IsPlayer(world, tribe))
        {
            hints.Notice(tribe, EntityNotices.NoBuildingSlot, 0, 0, 0);
        }
    }

    private PlacementResult PlaceTrap(PlacementRequest request)
    {
        int slot = request.Slot == PlacementRequest.NewSlot ? world.FreeTrapOrderSlot() : request.Slot;
        if (slot < 0 || slot >= world.Traps.Count)
        {
            return PlacementResult.NoSlot;
        }
        TrapOrder trap = world.Traps[slot];
        (trap.OwnerBuilding, trap.X, trap.Y, trap.Kind) = (request.TrapOwner, request.Cell.X, request.Cell.Y, request.Kind);
        if (!request.Immediate)
        {
            ArmouryOrders queue = world.Tribes[request.Tribe].Armouries[request.TrapQueue];
            int product = FirstTrapProduct + request.Kind - BuildingKindNumbers.FirstTrap;
            queue.SetPending(product, queue.Pending(product) + 1);
            world.Map.SetCell(request.Cell, new MapCell(CellContent.PendingTrap, 0, request.Tribe));
        }
        else
        {
            world.Map.SetCell(request.Cell, CellCode.FromCode(CellCode.Trap(request.Tribe, request.Kind), world));
        }
        return new PlacementResult(PlacementOutcome.Placed, slot);
    }

    private PlacementResult PlaceBuilding(PlacementRequest request, int slot)
    {
        if (request.Kind == BuildingKindNumbers.Armoury && !TakeArmouryQueue(request.Tribe, slot))
        {
            return PlacementResult.NoSlot;
        }
        Building building = world.Building(slot);
        Reset(building, request);
        bool player = TribeRoles.IsPlayer(world, request.Tribe);
        if (BuildingKindNumbers.IsSite(request.Kind))
        {
            if (!workLists.AddConstructionSite(slot))
            {
                ReportNoSlot(request.Tribe);
                building.HitPoints = 0;
                return PlacementResult.NoSlot;
            }
            building.Aux = request.SiteTarget;
            building.MaxHitPoints = kinds[building.Aux].MaxHitPoints[0];
            building.Level = SiteLevel;
        }
        else if (player)
        {
            BuildingKind kind = kinds[building.Kind];
            sight.RevealDisc(request.Tribe, FogLayer.Visible, building.X + (kind.Width / 2), building.Y + (kind.Height / 2),
                building.SightRadius);
        }
        FillFootprint(building);
        Settle(building, request, player);
        return new PlacementResult(PlacementOutcome.Placed, slot);
    }

    private bool TakeArmouryQueue(int tribe, int slot)
    {
        if (world.Tribes[tribe].FreeArmouryQueue(world.GrowsOnDemand) is not { } queue)
        {
            return false;
        }
        queue.Armoury = slot;
        return true;
    }

    /// <summary>A new building's state; occupants, flags and level go, the aux value and workers stay.</summary>
    private void Reset(Building building, PlacementRequest request)
    {
        (building.X, building.Y, building.Kind) = (request.Cell.X, request.Cell.Y, request.Kind);
        building.Flags = 0;
        building.Visibility = 0;
        building.OccupantCount = 0;
        building.FirstOccupant = 0;
        building.AlertFlags = 0;
        building.Level = request.Level != 0 ? request.Level : DefaultLevel(request.Kind);
        building.OperatorOrBeds = 0;
        building.RangeInfo = Occupancy.DefaultRange;
        building.WorkMode = BuildingWorkMode.Normal;
        building.HitPoints = building.MaxHitPoints = kinds[request.Kind].MaxHitPoints[0];
    }

    /// <summary>The mission's level for a kind placed without one (kinds without a table entry get level 1).</summary>
    private int DefaultLevel(int kind)
    {
        IReadOnlyList<int> levels = world.Content.Rules.Missions.Missions[world.Mission].DefaultBuildingLevels;
        return kind < levels.Count ? levels[kind] : DefaultLevelWithoutTable;
    }

    private void FillFootprint(Building building)
    {
        BuildingKind kind = kinds[building.Kind];
        for (int dy = 0; dy < kind.Height; dy++)
        {
            for (int dx = 0; dx < kind.Width; dx++)
            {
                world.Map.SetCell(new GridPoint(building.X + dx, building.Y + dy), MapCell.BuildingAt(building.Id));
            }
        }
    }

    private void Settle(Building building, PlacementRequest request, bool player)
    {
        TribeState tribe = world.Tribes[request.Tribe];
        if (building.Kind == BuildingKindNumbers.Hut)
        {
            int space = tribe[TribeCounter.LivingSpace] + HutLivingSpace;
            tribe[TribeCounter.LivingSpace] = world.UnitLimit is null ? space : Math.Min(space, MaxLivingSpace);
        }
        if (!world.Script.Active && request.Slot == PlacementRequest.NewSlot)
        {
            tribe[TribeCounter.Buildings]++;
            tribe[TribeCounter.Built]++;
        }
        if (building.Kind == BuildingKindNumbers.Hut)
        {
            couples.CancelCoupleAt(building);
        }
        if (request.Immediate && player)
        {
            hints.Notice(request.Tribe, EntityNotices.BuildingPlaced, request.Cell.X, request.Cell.Y, 0);
        }
    }
}
