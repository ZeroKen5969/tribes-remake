using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Buildings;

/// <summary>
/// Taking goods out of a tribe's stock for the work of a fetch action: the stock recipe of the
/// action (materials for the armoury, raw food for the kitchen, building material), or the
/// material an upgrade or repair costs.
/// </summary>
internal sealed class StoreKeeping
{
    /// <summary>The recipe of the upgrade and repair material (the action <see cref="WorkActions.FetchUpgradeMaterial"/>).</summary>
    private const int UpgradeMaterialRecipe = WorkActions.FetchUpgradeMaterial - WorkActions.FirstFetch;

    /// <summary>The recipes from this one on change weapons: recipe - this is the weapon taken.</summary>
    private const int FirstWeaponRecipe = 16;

    /// <summary>A kitchen fetch takes this much raw food at most (and at least 1 if there is any).</summary>
    private const int FoodLoad = 9, LeastFood = 1;

    /// <summary>The three building materials: wood, stones, bones (stock counters in this order).</summary>
    private const int MaterialCount = 3;

    /// <summary>A repair costs the build cost in proportion to the missing hit points, divided by this.</summary>
    private const int RepairCostDivisor = 2;

    /// <summary>The player's notice that the stock is short, and the hint channel that spaces it out.</summary>
    private const int StockShortNotice = 9, StockShortChannel = 1;

    private readonly GameWorld world;
    private readonly HintSystem hints;

    /// <summary>Keeps the stocks of <paramref name="world"/>.</summary>
    public StoreKeeping(GameWorld world, HintSystem hints)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
    }

    /// <summary>
    /// <paramref name="worker"/>'s fetch action <paramref name="action"/> takes its goods from the
    /// stock of <paramref name="tribe"/>; for upgrade or repair material, those of
    /// <paramref name="building"/>, whose job then knows how much work is needed. Returns false
    /// (taking nothing) when the stock is short; the worker then shows what is missing.
    /// </summary>
    public bool TakeFromStock(int action, Unit worker, Building building, int tribe)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(building);
        int recipe = action - WorkActions.FirstFetch;
        if (!InStock(recipe, worker, building, tribe))
        {
            return false;
        }
        TribeState stock = world.Tribes[tribe];
        if (recipe != UpgradeMaterialRecipe)
        {
            int food = action == WorkActions.FetchFood ? GridMath.Clamp(FoodLoad, LeastFood, stock[TribeCounter.Food]) : 0;
            foreach (CounterAmount cost in Recipe(recipe).Costs)
            {
                stock[cost.Counter] -= action == WorkActions.FetchFood ? food : cost.Amount;
            }
            return true;
        }
        TakeUpgradeMaterial(building, stock);
        return true;
    }

    /// <summary>
    /// Whether the stock holds the goods of <paramref name="recipe"/> (see <see cref="TakeFromStock"/>).
    /// An action that is not a fetch action has no recipe and fetches nothing.
    /// </summary>
    private bool InStock(int recipe, Unit worker, Building building, int tribe)
    {
        if (recipe != UpgradeMaterialRecipe && (recipe < 0 || recipe >= world.Content.Rules.Armoury.Recipes.Count))
        {
            return false;
        }
        TribeState stock = world.Tribes[tribe];
        if (recipe == UpgradeMaterialRecipe)
        {
            IReadOnlyList<int> costs = MaterialCosts(building);
            for (int m = 0; m < MaterialCount; m++)
            {
                if (stock[TribeCounter.Wood + m] < costs[m])
                {
                    ShowMissing(worker, (int)TribeCounter.Wood + m, tribe);
                    return false;
                }
            }
            return true;
        }
        foreach (CounterAmount cost in Recipe(recipe).Costs)
        {
            if (stock[cost.Counter] < cost.Amount && worker.Weapon != recipe - FirstWeaponRecipe)
            {
                if (recipe < FirstWeaponRecipe)
                {
                    ShowMissing(worker, (int)cost.Counter, tribe);
                }
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The material of an upgrade or repair leaves the stock and the job learns the work needed:
    /// the missing hit points of a repair, the hit points the next level adds for an upgrade.
    /// </summary>
    private void TakeUpgradeMaterial(Building building, TribeState stock)
    {
        IReadOnlyList<int> costs = MaterialCosts(building);
        for (int m = 0; m < MaterialCount; m++)
        {
            stock[TribeCounter.Wood + m] -= costs[m];
        }
        foreach (UpgradeJob job in world.Upgrades)
        {
            if (job.Building == building.Id)
            {
                job.Needed = building.WorkMode == BuildingWorkMode.Repairing
                    ? building.MaxHitPoints - building.HitPoints
                    : LevelHitPoints(building, building.Level) - LevelHitPoints(building, building.Level - 1);
                return;
            }
        }
    }

    /// <summary>What an upgrade costs, or a repair (the build cost in proportion to the missing hit points, halved).</summary>
    private IReadOnlyList<int> MaterialCosts(Building building)
    {
        BuildingKind kind = world.Content.Rules.Buildings.Kinds[building.Kind];
        if (building.WorkMode != BuildingWorkMode.Repairing)
        {
            return kind.UpgradeCost;
        }
        int missing = building.MaxHitPoints - building.HitPoints;
        var costs = new int[MaterialCount];
        for (int m = 0; m < MaterialCount; m++)
        {
            costs[m] = kind.BuildCost[m] * missing / (building.MaxHitPoints * RepairCostDivisor);
        }
        return costs;
    }

    /// <summary>
    /// The hit point limit of level index <paramref name="index"/> (0 = level 1). The original's
    /// table goes on with the build costs after the three levels, which a level 3 upgrade reads.
    /// </summary>
    private int LevelHitPoints(Building building, int index)
    {
        BuildingKind kind = world.Content.Rules.Buildings.Kinds[building.Kind];
        return index < kind.MaxHitPoints.Count ? kind.MaxHitPoints[index] : kind.BuildCost[index - kind.MaxHitPoints.Count];
    }

    /// <summary>The worker shows the goods it lacks; the player is told the stock is short (now and then).</summary>
    private void ShowMissing(Unit worker, int counter, int tribe)
    {
        worker.CarriedItem = world.Content.Rules.Armoury.StockCargo[counter].CarriedItem;
        HintState state = TribeRoles.Hints(world, tribe);
        if (state.Cooldown(StockShortChannel) < 0 && TribeRoles.IsPlayer(world, tribe))
        {
            state.SetCooldown(StockShortChannel, 0);
            hints.Notice(tribe, StockShortNotice, 0, 0, 0);
        }
    }

    private StockRecipe Recipe(int recipe) => world.Content.Rules.Armoury.Recipes[recipe];
}
