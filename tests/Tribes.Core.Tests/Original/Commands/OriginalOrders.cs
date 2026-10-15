using System;
using System.Linq;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Tribes.Legacy;

namespace Tribes.Core.Tests.Commands;

/// <summary>
/// Turns the order reports of the original's input handlers (PortHooks.orders) into new-rules
/// orders: the same intent, read from the values the handler was about to use.
/// </summary>
internal static class OriginalOrders
{
    private const int Player = 0;
    private const int FirstTrapKind = 20, LastTrapKind = 22, FirstSiteKind = 12, LastSiteKind = 19;
    private const int UpgradeEntry = 16, RepairEntry = 26, StopUpgradeEntry = 27, StopRepairEntry = 28;
    private const int CancelRallyEntry = 14, CancelAllOutEntry = 15, DeliverEntry = 29;
    private const int FoodHint = 14, RestHint = 15;

    /// <summary>
    /// The new rules' order for <paramref name="report"/>, or null when the report names nothing the
    /// player can order with those values (for example a construction site of another kind than the menu entry).
    /// </summary>
    public static GameOrder? From(GameContent content, int[] report)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(report);
        int[] v = report[1..];
        return report[0] switch
        {
            PortHooks.ORDER_UNITS => new MoveUnits(Player, new UnitSet(v[4..]), Cell(v, 1), v[3], v[0] == 1),
            PortHooks.ORDER_PLACE => Placement(content, v),
            PortHooks.ORDER_DEMOLISH => new DemolishBuilding(Player, v[0]),
            PortHooks.ORDER_BUILDING_WORK => new OrderBuildingWork(Player, v[0], v[1] switch
            {
                UpgradeEntry => BuildingWork.Upgrade,
                RepairEntry => BuildingWork.Repair,
                StopUpgradeEntry => BuildingWork.StopUpgrade,
                StopRepairEntry => BuildingWork.StopRepair,
                _ => throw new ArgumentException($"building work entry {v[1]}", nameof(report)),
            }),
            PortHooks.ORDER_EVICT => new EvictUnit(Player, v[0], v[1]),
            PortHooks.ORDER_ROSTER => v[1] == 1 ? new AppointChief(Player, v[0]) : new CallOutUnit(Player, v[0]),
            PortHooks.ORDER_ARMY_POINT => v[0] == 1 ? new CallArmy(Player, ArmyCall.AllOut, Cell(v, 2))
                : v[1] == 1 ? new CallArmy(Player, ArmyCall.Rally, Cell(v, 2)) : null,
            PortHooks.ORDER_ARMY_CANCEL => new CancelArmyCall(Player, v[1] == CancelRallyEntry ? ArmyCall.Rally
                : v[1] == CancelAllOutEntry ? ArmyCall.AllOut : throw new ArgumentException($"army entry {v[1]}", nameof(report))),
            PortHooks.ORDER_TASK => v[1] == OrderMenus.PlaceSpell
                ? new CastSpellAt(Player, v[0], v[1], Cell(v, 3))
                : new CastSpell(Player, v[0], v[1], v[2]),
            PortHooks.ORDER_ARMOURY_COUNT => new SetArmouryOrder(Player, v[0], v[2], v[3]),
            PortHooks.ORDER_FETCH => v[1] == DeliverEntry
                ? new DeliverToStore(Player, new UnitSet(v[4..]), v[0], Cell(v, 2))
                : new FetchWeapon(Player, new UnitSet(v[4..]), v[0], v[1], Cell(v, 2)),
            PortHooks.ORDER_HINT_ANSWER => v[0] == FoodHint ? new TendNeed(Player, v[1], UnitNeed.Food)
                : v[0] == RestHint ? new TendNeed(Player, v[1], UnitNeed.Rest) : null,
            PortHooks.ORDER_HINT_OPTION => new SetHints(Player, v[0] == 1),
            PortHooks.ORDER_PANEL_HELP => new ViewedHelp(Player),
            PortHooks.ORDER_SCENE_CONTINUE => new AdvanceScene(Player),
            _ => null,
        };
    }

    /// <summary>
    /// Applies the order of <paramref name="report"/> to <paramref name="world"/> (with fresh rule
    /// systems that play story scenes) and names it with its outcome; null when there is no order.
    /// </summary>
    public static string? Rewrite(GameContent content, int[] report, GameWorld world)
    {
        GameOrder? order = From(content, report);
        if (order == null)
        {
            return null;
        }
        var systems = new RuleSystems(world);
        OrderOutcome outcome = systems.ApplyOrder(order);
        string name = order.GetType().Name;
        return outcome == OrderOutcome.Applied ? name : $"{name} ({outcome})";
    }

    private static GameOrder? Placement(GameContent content, int[] v)
    {
        int kind = v[0];
        var origin = Cell(v, 1);
        if (kind >= FirstTrapKind && kind <= LastTrapKind)
        {
            return new PlaceTrap(Player, v[3], kind, origin);
        }
        int planned = v[5];
        bool siteOfEntry = kind >= FirstSiteKind && kind <= LastSiteKind && planned >= 0
            && planned < content.Rules.Buildings.Kinds.Count && content.Rules.Buildings.Kinds[planned].SiteKind == kind;
        return siteOfEntry ? new PlaceBuilding(Player, planned, origin) : null;
    }

    private static GridPoint Cell(int[] values, int at) => new(values[at], values[at + 1]);

    /// <summary>The report's order kind and values, readable.</summary>
    public static string Describe(int[] report) => $"[{string.Join(",", report.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)))}]";
}
