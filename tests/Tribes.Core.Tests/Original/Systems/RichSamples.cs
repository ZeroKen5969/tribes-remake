using System;
using System.Collections.Generic;
using System.Linq;
using Tribes.Legacy;
using Tribes.Trace;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// Sampled states made richer with the original's own code: the key-monkey games stay in the first
/// mission with a handful of units, so every building kind is placed for both tribes, computer units
/// and children are born, units are put into buildings, a couple is formed, buildings are upgraded,
/// repaired and destroyed, and a unit starts dying. The parity tests then run original and rewrite
/// from these states.
/// </summary>
internal static class RichSamples
{
    private const int Kinds = 12, SiteKind = 18, SiteTarget = 2, PlayerTribe = 0, ComputerTribe = 1;
    private const int HitPoints = 2, Kind = 6, Occupants = 9, FirstOccupant = 10, LevelField = 12, Aux = 14;
    private const int UnitX = 0, UnitY = 1, Age = 14, Gender = 15, UnitHp = 19, Action = 28, Animation = 29;
    private const int SubState = 13, CarriedItem = 35, CarriedAmount = 36, TargetIndex = 44, NextOccupant = 45, PairLink = 82;

    private static readonly object Gate = new();
    private static IReadOnlyList<LegacySample>? all;

    /// <summary>Every second sample of <see cref="LegacySamples.All"/>, enriched.</summary>
    public static IReadOnlyList<LegacySample> All
    {
        get
        {
            lock (Gate)
            {
                return all ??= LegacySamples.All.Where((_, i) => i % 2 == 1).Select(Enrich).ToList();
            }
        }
    }

    /// <summary>The base samples and the rich ones.</summary>
    public static IEnumerable<LegacySample> AllStates => LegacySamples.All.Where((_, i) => i % 4 == 0).Concat(All);

    private static LegacySample Enrich(LegacySample sample)
    {
        GameCanvas game = sample.Restore();
        var placed = new List<int>();
        foreach (int tribe in new[] { PlayerTribe, ComputerTribe })
        {
            for (int kind = 0; kind < Kinds; kind++)
            {
                int slot = Place(game, tribe, kind, 1 + ((kind + tribe) % 2));
                if (slot > 0)
                {
                    placed.Add(slot);
                }
            }
            game.bArr_r[game.b_as] = SiteTarget;
            game.world_a.i_v = SiteTarget;
            Place(game, tribe, SiteKind, 1);
        }
        SpawnUnits(game);
        FillBuildings(game, placed);
        FormCouple(game, placed);
        Disturb(game, placed);
        return sample with { Frame = sample.Frame + 1, State = LegacySnapshot.Capture(game) };
    }

    /// <summary>Places a building of <paramref name="kind"/> at the first free place found; its slot or 0.</summary>
    private static int Place(GameCanvas game, int tribe, int kind, int level)
    {
        int startX = tribe == PlayerTribe ? 0 : game.s_c / 2;
        for (int y = 1; y < game.s_d - 1; y++)
        {
            for (int i = 0; i < game.s_c; i++)
            {
                int x = (startX + i) % game.s_c;
                int slot = game.a_BIIIIZI((sbyte)tribe, kind, x, y, level, true, -1);
                if (slot > 0)
                {
                    return slot;
                }
                if (slot == -1)
                {
                    return 0;
                }
            }
        }
        return 0;
    }

    private static void SpawnUnits(GameCanvas game)
    {
        (int Tribe, int Age, int Traits)[] births =
        {
            (ComputerTribe, 25, 0x45), (ComputerTribe, 40, 0x2A), (ComputerTribe, 6, 0), (ComputerTribe, 30, 0x7F),
            (ComputerTribe, 20, 0x13), (PlayerTribe, 4, 0), (PlayerTribe, 22, 0x41), (PlayerTribe, 50, 0x22),
        };
        foreach (var (tribe, age, traits) in births)
        {
            (int x, int y) = FreeCell(game, tribe == PlayerTribe ? 2 : game.s_c - 3, game.s_d / 2);
            game.a_BBBBB((sbyte)tribe, (sbyte)x, (sbyte)y, (sbyte)age, (sbyte)traits);
        }
    }

    /// <summary>Puts one adult of the building's tribe into each placed building, as a birth inside a hut does.</summary>
    private static void FillBuildings(GameCanvas game, List<int> placed)
    {
        var used = new HashSet<int>();
        foreach (int building in placed.Where((_, i) => i % 2 == 0).Where(b => HasRoom(game, b)))
        {
            int tribe = (building - 1) / 30;
            int unit = LegacySamples.LiveUnits(game).FirstOrDefault(id => (id - 1) / 50 == tribe && !used.Contains(id)
                && LegacySamples.Field(game, Age, id) >= 12 && game.bArr2_c[Field(game, UnitY, id)][Field(game, UnitX, id)] == id);
            if (unit == 0)
            {
                continue;
            }
            used.Add(unit);
            Enter(game, unit, building);
        }
    }

    /// <summary>Whether a unit may enter <paramref name="building"/> (it has work positions at its level).</summary>
    private static bool HasRoom(GameCanvas game, int building)
    {
        int kind = game.bArr_e[8383 + (Kind * 61) + building];
        int level = game.bArr_e[8383 + (LevelField * 61) + building];
        return LegacySamples.Content.Rules.Buildings.Kinds[kind].WorkerSlots[level - 1] > 0;
    }

    private static void Enter(GameCanvas game, int unit, int building)
    {
        sbyte[] s = game.bArr_e;
        game.bArr2_c[Field(game, UnitY, unit)][Field(game, UnitX, unit)] = 0;
        s[(TargetIndex * 101) + unit] = (sbyte)building;
        s[8383 + (Occupants * 61) + building]++;
        game.world_a.i_v = unit;
        game.world_a.G_();
        s[(NextOccupant * 101) + unit] = s[8383 + (FirstOccupant * 61) + building];
        s[8383 + (FirstOccupant * 61) + building] = (sbyte)unit;
        s[(SubState * 101) + unit] = -3;
        s[(Animation * 101) + unit] = 71;
        s[(Action * 101) + unit] = 59;
    }

    private static void FormCouple(GameCanvas game, List<int> placed)
    {
        int hut = placed.FirstOrDefault(b => (b - 1) / 30 == PlayerTribe && game.bArr_e[8383 + (Kind * 61) + b] == 2);
        var adults = LegacySamples.LiveUnits(game).Where(id => id <= 50 && Field(game, Age, id) >= 12
            && Field(game, UnitHp, id) > 0 && game.bArr2_c[Field(game, UnitY, id)][Field(game, UnitX, id)] == id).ToList();
        int man = adults.FirstOrDefault(id => Field(game, Gender, id) == 0);
        int woman = adults.FirstOrDefault(id => Field(game, Gender, id) == 1);
        if (hut == 0 || man == 0 || woman == 0)
        {
            return;
        }
        sbyte[][] couples = game.world_a.bArr2_c;
        (couples[0][0], couples[1][0], couples[4][0], couples[5][0]) = ((sbyte)man, (sbyte)woman, 1, (sbyte)hut);
        game.bArr_e[(PairLink * 101) + man] = 2;
        game.bArr_e[(PairLink * 101) + woman] = 3;
        game.bArr_e[8383 + (Aux * 61) + hut] = 1;
        game.bArr_e[(Action * 101) + woman] = 109;
        game.bArr_e[(Action * 101) + man] = 110;
        game.bArr_e[(CarriedItem * 101) + man] = 15;
        game.bArr_e[(CarriedAmount * 101) + man] = 1;
    }

    /// <summary>Upgrades one building, repairs another, destroys a third and lets one unit start dying.</summary>
    private static void Disturb(GameCanvas game, List<int> placed)
    {
        if (placed.Count > 3)
        {
            game.b_IZ(placed[1], true);
            game.bArr_e[8383 + (HitPoints * 61) + placed[3]] = 3;
            game.b_IZ(placed[3], false);
        }
        if (placed.Count > 5)
        {
            game.c_IZ(placed[5], true);
        }
        int victim = LegacySamples.LiveUnits(game).LastOrDefault();
        if (victim > 0)
        {
            game.a_B((sbyte)victim);
        }
    }

    private static (int X, int Y) FreeCell(GameCanvas game, int x0, int y0)
    {
        for (int r = 0; r < Math.Max(game.s_c, game.s_d); r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    if (x >= 0 && y >= 0 && x < game.s_c && y < game.s_d && game.bArr2_c[y][x] == 0)
                    {
                        return (x, y);
                    }
                }
            }
        }
        return (x0, y0);
    }

    private static int Field(GameCanvas game, int field, int id) => LegacySamples.Field(game, field, id);
}
