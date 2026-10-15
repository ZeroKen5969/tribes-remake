using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Core.Content.Rules;

/// <summary>
/// Something an armoury makes: 0 club, 1 stone, 2 spear, 3 stone axe, 4 bone mace, 5 tomahawk,
/// 6 net trap, 7 spring trap, 8 pit trap.
/// </summary>
/// <param name="Id">Product number 0..8.</param>
/// <param name="CraftAction">The action that makes it; 0 for the stone, which is not made.</param>
/// <param name="StockCounter">
/// The tribe counter finished items go to (the carried item on delivery is counter - 5 + 8); the
/// traps name the population counter, which their delivery is not known to use.
/// </param>
public sealed record ArmouryProduct(int Id, int CraftAction, TribeCounter StockCounter);

/// <summary>
/// What a unit takes out of the tribe's stock for a fetch action (actions 28..49: recipe = action - 28).
/// 28..35 materials for the armoury products, 36 wood and stones (use unknown), 37 raw food for the
/// kitchen, 38..43 building material, 44..49 changing to weapon 0..5.
/// </summary>
/// <param name="Id">Recipe number 0..21.</param>
/// <param name="Costs">Counters and amounts taken from the stock.</param>
/// <param name="CargoItem">For recipes 0..15: the item kind the unit then carries; null for the others.</param>
/// <param name="CargoAmount">For recipes 0..15: how much it then carries (9 is a full load); null for the others.</param>
public sealed record StockRecipe(int Id, IReadOnlyList<CounterAmount> Costs, int? CargoItem, int? CargoAmount);

/// <summary>The item shown as carried when a unit fetches from one stock counter (also when the stock is short).</summary>
/// <param name="Counter">The stock counter.</param>
/// <param name="CarriedItem">The carried item kind.</param>
public sealed record StockCargo(TribeCounter Counter, int CarriedItem);

/// <summary>A weapon a unit without one takes from the stock.</summary>
/// <param name="Weapon">The weapon kind.</param>
/// <param name="StockCost">How many items the stock loses (stones: 5).</param>
public sealed record AutoEquipChoice(int Weapon, int StockCost);

/// <summary>A trap kind (net, spring, pit).</summary>
/// <param name="Id">Trap number 0..2 (building kinds 20..22).</param>
/// <param name="Damage">Damage to a unit caught in it.</param>
public sealed record TrapKind(int Id, int Damage);

/// <summary>Armoury products, stock recipes, equipment choices, spells and traps.</summary>
/// <param name="Products">Armoury products by number.</param>
/// <param name="Recipes">Stock recipes by number.</param>
/// <param name="StockCargo">The item shown for each stock counter 0..10.</param>
/// <param name="AutoEquipOrder">
/// Weapons tried in order by a unit without one: positions 0..5 when its melee skill is at least
/// its ranged skill, else 3..8.
/// </param>
/// <param name="AiWeaponOrderPriority">
/// Products the computer orders weapons from, in order: the first order slot looks at all five,
/// the second only at the last two (thrown weapons).
/// </param>
/// <param name="SpellActions">The sanctuary's spells 0..3 (healing, protection, love potion, disease) as actions.</param>
/// <param name="Traps">Trap kinds by number.</param>
public sealed record ArmouryTables(IReadOnlyList<ArmouryProduct> Products, IReadOnlyList<StockRecipe> Recipes,
    IReadOnlyList<StockCargo> StockCargo, IReadOnlyList<AutoEquipChoice> AutoEquipOrder,
    IReadOnlyList<int> AiWeaponOrderPriority, IReadOnlyList<int> SpellActions, IReadOnlyList<TrapKind> Traps);
