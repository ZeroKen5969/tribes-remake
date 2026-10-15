using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// A unit fetching from its tribe's stock for its task: material for an armoury product, food for
/// the kitchen, building material for a site, the material of an upgrade or repair, or another
/// weapon; and a fighter taking a weapon out of the stock.
/// </summary>
internal sealed class WorkMaterials
{
    /// <summary>Fetch actions are numbered from this one; recipe = action - this.</summary>
    public const int FirstFetch = 28;

    /// <summary>The fetch action for upgrade and repair material, and its recipe.</summary>
    public const int FetchUpgradeMaterial = 99, UpgradeMaterialRecipe = FetchUpgradeMaterial - FirstFetch;

    /// <summary>The kitchen fetch: it takes up to a full load of raw food.</summary>
    private const int FetchFood = 37, FoodLoad = 9, LeastFood = 1;

    /// <summary>The recipes from this one on change weapons (recipe - this = the weapon).</summary>
    private const int FirstWeaponRecipe = 16;

    /// <summary>The first weapon change action (44 + weapon).</summary>
    private const int FirstWeaponChange = 44;

    /// <summary>Upgrade material is carried as a full load of wood.</summary>
    private const int MaterialLoad = 9, MaterialItem = 2;

    /// <summary>Weapons up to this one are given back to the stock when changed.</summary>
    private const int LastStockedWeapon = 5;

    /// <summary>The player's notice that the stock is short, and the hint channel that spaces it out.</summary>
    private const int StockShortNotice = 9, StockShortChannel = 1;

    /// <summary>A unit whose ranged skill is above its melee skill tries the auto-equip list from this entry.</summary>
    private const int RangedEquipStart = 3, EquipChoices = 6;

    private readonly GameWorld world;
    private readonly HintSystem hints;
    private readonly ArmouryTables armoury;
    private readonly IReadOnlyList<BuildingKind> kinds;

    /// <summary>Hands out the stock of <paramref name="world"/>.</summary>
    public WorkMaterials(GameWorld world, HintSystem hints)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        armoury = world.Content.Rules.Armoury;
        kinds = world.Content.Rules.Buildings.Kinds;
    }

    /// <summary>
    /// Whether the stock of <paramref name="unit"/>'s tribe holds the goods of <paramref name="recipe"/>
    /// for the building at its task site (upgrade material) or a weapon change it does not already
    /// have; when it does not, the unit shows what is missing and the player hears about it.
    /// </summary>
    public bool InStock(Unit unit, int recipe)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (recipe == UpgradeMaterialRecipe)
        {
            if (TaskSiteBuilding(unit) is not { } site)
            {
                return false;
            }
            if (MaterialMissing(site, unit.Tribe) is { } missing)
            {
                Show(unit, missing);
                return false;
            }
            return true;
        }
        if (recipe < 0 || recipe >= armoury.Recipes.Count)
        {
            return false;
        }
        foreach (CounterAmount cost in armoury.Recipes[recipe].Costs)
        {
            if (world.Tribes[unit.Tribe][cost.Counter] < cost.Amount && unit.Weapon != recipe - FirstWeaponRecipe)
            {
                if (recipe < FirstWeaponRecipe)
                {
                    Show(unit, (int)cost.Counter);
                }
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// <paramref name="unit"/>'s fetch action takes its goods: the costs leave the stock and the unit
    /// carries the goods (or changes its weapon, giving the old one back). For upgrade material the
    /// job at its task site learns the work needed. Returns false when the stock is short.
    /// </summary>
    public bool Fetch(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int recipe = unit.Action - FirstFetch;
        if (!InStock(unit, recipe))
        {
            return false;
        }
        TribeState stock = world.Tribes[unit.Tribe];
        int food = unit.Action == FetchFood ? GridMath.Clamp(FoodLoad, LeastFood, stock[TribeCounter.Food]) : 0;
        if (recipe == UpgradeMaterialRecipe)
        {
            TakeUpgradeMaterial(TaskSiteBuilding(unit)!, stock);
            unit.CarriedAmount = MaterialLoad;
            unit.CarriedItem = MaterialItem;
            return true;
        }
        foreach (CounterAmount cost in armoury.Recipes[recipe].Costs)
        {
            stock[cost.Counter] -= unit.Action == FetchFood ? food : cost.Amount;
        }
        if (unit.Action >= FirstWeaponChange)
        {
            ChangeWeapon(unit, unit.Action - FirstWeaponChange);
        }
        else
        {
            StockRecipe taken = armoury.Recipes[recipe];
            unit.CarriedAmount = unit.Action == FetchFood ? food : taken.CargoAmount ?? 0;
            unit.CarriedItem = taken.CargoItem ?? 0;
        }
        return true;
    }

    /// <summary>
    /// A fighter without a weapon takes the first one of the auto-equip list the stock has (a
    /// unit better at ranged fighting starts further down the list).
    /// </summary>
    public void TakeWeapon(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int start = unit.MeleeSkill < unit.RangedSkill ? RangedEquipStart : 0;
        TribeState stock = world.Tribes[unit.Tribe];
        for (int i = start; i < start + EquipChoices; i++)
        {
            AutoEquipChoice choice = armoury.AutoEquipOrder[i];
            TribeCounter counter = armoury.Products[choice.Weapon].StockCounter;
            if (stock[counter] >= choice.StockCost)
            {
                stock[counter] -= choice.StockCost;
                unit.Weapon = choice.Weapon;
                return;
            }
        }
    }

    private void ChangeWeapon(Unit unit, int weapon)
    {
        if (unit.Weapon <= LastStockedWeapon)
        {
            WeaponKind old = world.Content.Rules.Weapons.Weapons[unit.Weapon];
            world.Tribes[unit.Tribe][armoury.Products[unit.Weapon].StockCounter] += old.RefundAmount ?? 0;
        }
        unit.Weapon = weapon;
    }

    /// <summary>The building at <paramref name="unit"/>'s task site, or null when there is none.</summary>
    private Building? TaskSiteBuilding(Unit unit)
    {
        var site = new GridPoint(unit.TaskSiteX, unit.TaskSiteY);
        if (!world.Map.Contains(site))
        {
            return null;
        }
        int slot = CellCode.BuildingOf(CellCode.At(world.Map, site));
        return slot >= 1 && slot <= world.BuildingSlots ? world.Building(slot) : null;
    }

    /// <summary>The stock counter of the first material the upgrade or repair of <paramref name="site"/> lacks, or null.</summary>
    private int? MaterialMissing(Building site, int tribe)
    {
        IReadOnlyList<int> costs = MaterialCosts(site);
        for (int m = 0; m < costs.Count; m++)
        {
            if (world.Tribes[tribe][TribeCounter.Wood + m] < costs[m])
            {
                return (int)TribeCounter.Wood + m;
            }
        }
        return null;
    }

    /// <summary>
    /// The material leaves the stock, and the job of <paramref name="site"/> learns (as a negative
    /// count still to be confirmed by the site) the work needed: the missing hit points of a repair,
    /// the hit points the next level adds for an upgrade.
    /// </summary>
    private void TakeUpgradeMaterial(Building site, TribeState stock)
    {
        IReadOnlyList<int> costs = MaterialCosts(site);
        for (int m = 0; m < costs.Count; m++)
        {
            stock[TribeCounter.Wood + m] -= costs[m];
        }
        foreach (UpgradeJob job in world.Upgrades)
        {
            if (job.Building == site.Id)
            {
                job.Needed = site.WorkMode == BuildingWorkMode.Repairing
                    ? -(site.MaxHitPoints - site.HitPoints)
                    : LevelHitPoints(site, site.Level - 1) - LevelHitPoints(site, site.Level);
                return;
            }
        }
    }

    /// <summary>What an upgrade costs, or a repair (the build cost in proportion to the missing hit points, halved).</summary>
    private IReadOnlyList<int> MaterialCosts(Building site)
    {
        BuildingKind kind = kinds[site.Kind];
        if (site.WorkMode != BuildingWorkMode.Repairing)
        {
            return kind.UpgradeCost;
        }
        int missing = site.MaxHitPoints - site.HitPoints;
        var costs = new int[kind.BuildCost.Count];
        for (int m = 0; m < costs.Length; m++)
        {
            costs[m] = kind.BuildCost[m] * missing / (site.MaxHitPoints * 2);
        }
        return costs;
    }

    /// <summary>
    /// The hit point limit of level index <paramref name="index"/> (0 = level 1); the original's table
    /// goes on with the build costs after the three levels, which a level 3 upgrade reads.
    /// </summary>
    private int LevelHitPoints(Building site, int index)
    {
        BuildingKind kind = kinds[site.Kind];
        return index < kind.MaxHitPoints.Count ? kind.MaxHitPoints[index] : kind.BuildCost[index - kind.MaxHitPoints.Count];
    }

    /// <summary>The unit shows the goods of <paramref name="counter"/> it lacks; the player is told the stock is short (now and then).</summary>
    private void Show(Unit unit, int counter)
    {
        unit.CarriedItem = armoury.StockCargo[counter].CarriedItem;
        HintState state = TribeRoles.Hints(world, unit.Tribe);
        if (state.Cooldown(StockShortChannel) < 0 && TribeRoles.IsPlayer(world, unit.Tribe))
        {
            state.SetCooldown(StockShortChannel, 0);
            hints.Notice(unit.Tribe, StockShortNotice, 0, 0, 0);
        }
    }
}
