using System.Linq;
using Tribes.Legacy;
using static Tribes.Core.Tests.Systems.LegacyUnits;
using static Tribes.Parity.EntityFields;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// Puts a sampled original state into a situation of a unit's work (a witch doctor with a spell to
/// cast, an armoury with orders, a unit carrying a load...), written straight into the original's
/// records so both sides of a comparison start from it.
/// </summary>
internal static class WorkScenarios
{
    /// <summary>Building field numbers the scenarios set.</summary>
    public const int BuildingX = 0, BuildingY = 1, BuildingHitPoints = 2, BuildingKind = 6, BuildingOperator = 7;
    public const int BuildingOccupants = 9, BuildingFirstOccupant = 10, BuildingLevel = 12, BuildingAux = 14;

    /// <summary>Building kinds the scenarios use.</summary>
    public const int Armoury = 0, Hut = 2, Kitchen = 4, Store = 6, Sanctuary = 8, Tower = 11;

    /// <summary>Field <paramref name="field"/> of building <paramref name="id"/>.</summary>
    public static int BuildingField(GameCanvas g, int field, int id) => g.bArr_e[BuildingBase + (field * BuildingStride) + id];

    /// <summary>Sets field <paramref name="field"/> of building <paramref name="id"/>.</summary>
    public static void SetBuilding(GameCanvas g, int field, int id, int value) =>
        g.bArr_e[BuildingBase + (field * BuildingStride) + id] = unchecked((sbyte)value);

    /// <summary>A standing building of <paramref name="kind"/> of <paramref name="tribe"/>, or 0.</summary>
    public static int FindBuilding(GameCanvas g, int tribe, int kind) =>
        Enumerable.Range((tribe * 30) + 1, 30).FirstOrDefault(b =>
            BuildingField(g, BuildingHitPoints, b) > 0 && BuildingField(g, BuildingKind, b) == kind
            && (tribe != 0 || Explored(g, BuildingField(g, BuildingX, b), BuildingField(g, BuildingY, b))));

    /// <summary>Whether the player has explored cell (<paramref name="x"/>, <paramref name="y"/>) (what the player's units see of their own buildings).</summary>
    public static bool Explored(GameCanvas g, int x, int y) => (g.iArr3_a[0][x / 32][y] & (1 << (x % 32))) != 0;

    /// <summary>Writes task order <paramref name="slot"/> of <paramref name="tribe"/>.</summary>
    public static void SetTaskOrder(GameCanvas g, int tribe, int slot, int building, int kind, int a, int b)
    {
        sbyte[][] orders = g.bArr3_b[tribe];
        orders[0][slot] = (sbyte)building;
        orders[1][slot] = (sbyte)kind;
        orders[2][slot] = (sbyte)a;
        orders[3][slot] = (sbyte)b;
    }

    /// <summary>
    /// Makes <paramref name="unit"/> the witch doctor of a sanctuary of its tribe, standing on its
    /// cell, with a spell order of <paramref name="spell"/> (0..3) at <paramref name="target"/>;
    /// returns the sanctuary or 0 when the tribe has none.
    /// </summary>
    public static int WitchDoctor(GameCanvas g, int unit, int spell, int target)
    {
        int tribe = TribeOf(unit);
        int sanctuary = FindBuilding(g, tribe, Sanctuary);
        if (sanctuary == 0)
        {
            return 0;
        }
        SetBuilding(g, BuildingOperator, sanctuary, unit);
        WorkAt(g, unit, sanctuary);
        Set(g, ActionField, unit, 57);
        Set(g, SubState, unit, 0);
        Set(g, 40, unit, sanctuary);
        int free = Enumerable.Range(0, 30).First(i => g.bArr3_b[tribe][0][i] == 0 || g.bArr3_b[tribe][0][i] == sanctuary);
        SetTaskOrder(g, tribe, free, sanctuary, spell, spell == 3 ? Get(g, X, target) : target, Get(g, Y, target));
        return sanctuary;
    }

    /// <summary>
    /// Makes <paramref name="unit"/> the worker of an armoury of its tribe with <paramref name="count"/>
    /// items of <paramref name="product"/> ordered; returns the armoury or 0.
    /// </summary>
    public static int ArmouryWorker(GameCanvas g, int unit, int product, int count)
    {
        int tribe = TribeOf(unit);
        int armoury = FindBuilding(g, tribe, Armoury);
        if (armoury == 0)
        {
            return 0;
        }
        SetBuilding(g, BuildingOperator, armoury, unit);
        WorkAt(g, unit, armoury);
        Set(g, ActionField, unit, 58);
        Set(g, SubState, unit, 0);
        sbyte[][] slots = g.bArr3_a[tribe];
        sbyte[] slot = slots.FirstOrDefault(s => s[11] == armoury) ?? slots.First(s => s[11] <= 0);
        slot[11] = (sbyte)armoury;
        slot[product] = (sbyte)count;
        slot[10] = (sbyte)product;
        return armoury;
    }

    /// <summary>Makes <paramref name="building"/> the task site of <paramref name="unit"/>, as entering it to work does.</summary>
    public static void WorkAt(GameCanvas g, int unit, int building)
    {
        Set(g, 62, unit, BuildingField(g, BuildingX, building));
        Set(g, 63, unit, BuildingField(g, BuildingY, building));
    }
}
