using System;
using System.Collections.Generic;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>A unit taking a weapon out of its tribe's stock.</summary>
internal sealed class StockArming
{
    /// <summary>Choices tried by a unit, from its first one (0 for melee fighters, 3 for ranged ones).</summary>
    private const int ChoicesTried = 6;

    /// <summary>The first choice of a unit whose ranged skill is above its melee skill.</summary>
    private const int FirstRangedChoice = 3;

    private readonly GameWorld world;
    private readonly IReadOnlyList<AutoEquipChoice> choices;
    private readonly IReadOnlyList<ArmouryProduct> products;

    /// <summary>Arms units of <paramref name="world"/>.</summary>
    public StockArming(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        choices = world.Content.Rules.Armoury.AutoEquipOrder;
        products = world.Content.Rules.Armoury.Products;
    }

    /// <summary>
    /// Gives <paramref name="unit"/> the first weapon of its choices that the stock has enough of
    /// (melee weapons first unless its ranged skill is higher) and takes it out of the stock;
    /// returns whether it got one.
    /// </summary>
    public bool Arm(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        TribeState tribe = world.Tribes[unit.Tribe];
        int first = unit.MeleeSkill < unit.RangedSkill ? FirstRangedChoice : 0;
        for (int i = first; i < first + ChoicesTried; i++)
        {
            AutoEquipChoice choice = choices[i];
            TribeCounter stock = products[choice.Weapon].StockCounter;
            if (tribe[stock] >= choice.StockCost)
            {
                tribe[stock] -= choice.StockCost;
                unit.Weapon = choice.Weapon;
                return true;
            }
        }
        return false;
    }
}
