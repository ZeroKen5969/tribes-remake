using System;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Map;

/// <summary>
/// The result of checking an order to a cell: the hint that explains why the unit refuses it (or
/// <see cref="TargetChecks.NoRefusal"/>), and the category whose target action applies.
/// </summary>
/// <param name="RefusalHint">Hint number, or <see cref="TargetChecks.NoRefusal"/>.</param>
/// <param name="Category">The probe's category, or <see cref="CellCategories.OccupiedBuilding"/> / <see cref="CellCategories.BusyBuilding"/>.</param>
internal readonly record struct OrderCheck(int RefusalHint, int Category);

/// <summary>Checks whether a unit accepts being sent to a cell.</summary>
internal sealed class TargetChecks
{
    /// <summary>The order is accepted.</summary>
    public const int NoRefusal = -1;

    /// <summary>Refusal hints (rows of <c>GameRules.Story.Hints</c>).</summary>
    private const int GymNeedsMeleeWeapon = 6, RangeNeedsThrowingWeapon = 7, FishingNeedsSpear = 8,
        NurseryIsForChildren = 9, ChiefsHutIsForTheChief = 10, ChildrenStayOut = 11, HutIsBusy = 13;

    /// <summary>Building kinds with an entry rule.</summary>
    private const int Gym = 1, Range = 5, Nursery = 10;

    /// <summary>Units of this age or older are too old for the nursery.</summary>
    private const int NurseryAgeLimit = 11;

    /// <summary>A hut whose birth stage is above this is busy with a birth.</summary>
    private const int HutBirthStageFree = 1;

    /// <summary>The projectile kind of a melee weapon (<see cref="WeaponKind.ProjectileKind"/>).</summary>
    private const int MeleeProjectile = -1;

    /// <summary>The weapon a fisher needs.</summary>
    private const int Spear = 2;

    private readonly GameWorld world;

    /// <summary>Checks orders in <paramref name="world"/>.</summary>
    public TargetChecks(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Whether <paramref name="unit"/> refuses to be sent to what <paramref name="probe"/> found, and
    /// which category's target action applies: children only enter the nursery; a hut with a birth
    /// on its way, a gym without a melee weapon, a range without a thrown weapon, the nursery for
    /// the older and the chief's hut for anyone but the chief (the player's, in the campaign) are refused; a building run
    /// by another unit or being worked on takes its other actions; fishing needs a spear.
    /// </summary>
    public OrderCheck CheckOrder(Unit unit, CellProbe probe)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (probe.OtherSide)
        {
            return new OrderCheck(NoRefusal, probe.Category);
        }
        if (probe.Category == CellCategories.Building)
        {
            return CheckBuilding(unit, probe);
        }
        if (probe.Category == CellCategories.ResourceNode && probe.SubKind == NodeKinds.FishingSpot && unit.Weapon != Spear)
        {
            return new OrderCheck(FishingNeedsSpear, probe.Category);
        }
        return new OrderCheck(NoRefusal, probe.Category);
    }

    private OrderCheck CheckBuilding(Unit unit, CellProbe probe)
    {
        Building building = world.Building(probe.Slot);
        int kind = probe.SubKind;
        if (kind != Nursery && unit.Age < UnitAges.Adult)
        {
            return new OrderCheck(ChildrenStayOut, probe.Category);
        }
        if (kind == BuildingKindNumbers.Hut && building.WorkMode == BuildingWorkMode.Normal
            && building.Aux > HutBirthStageFree && unit.Action != ActionNumbers.LastCoupleAction)
        {
            return new OrderCheck(HutIsBusy, probe.Category);
        }
        if (building.WorkMode is BuildingWorkMode.Upgrading or BuildingWorkMode.Repairing)
        {
            return new OrderCheck(NoRefusal, CellCategories.BusyBuilding);
        }
        int projectile = world.Content.Rules.Weapons.Weapons[unit.Weapon].ProjectileKind;
        int refusal = kind switch
        {
            Gym when projectile != MeleeProjectile => GymNeedsMeleeWeapon,
            Range when projectile < 0 => RangeNeedsThrowingWeapon,
            Nursery when unit.Age >= NurseryAgeLimit => NurseryIsForChildren,
            BuildingKindNumbers.ChiefsHut when unit.Id != world.Tribes[TribeRoles.PlayerOf(world, unit.Tribe)][TribeCounter.Chief]
                => ChiefsHutIsForTheChief,
            _ => NoRefusal,
        };
        if (refusal != NoRefusal)
        {
            return new OrderCheck(refusal, probe.Category);
        }
        bool runByOther = building.OperatorOrBeds > 0 && building.OperatorOrBeds != unit.Id;
        return new OrderCheck(NoRefusal, runByOther ? CellCategories.OccupiedBuilding : probe.Category);
    }
}
