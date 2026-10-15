using System.Collections.Generic;
using System.Linq;
using Tribes.Legacy;
using Tribes.Trace;
using static Tribes.Parity.EntityFields;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// States in which the buildings have work going on: in the rich samples (every kind placed for
/// both tribes) adults are put at work positions of sites, kitchens, stores, busy buildings and
/// huts (a couple in bed), sites get deliveries and hit points at every stage, upgrades need,
/// lack or have done their work, a repair is cancelled. Each state is taken with the frame counter
/// on a building's recount and builder-call frames, and is also played on by the original.
/// </summary>
internal static class BuildingSamples
{
    /// <summary>Frames a state is played on, and frames between two of its samples.</summary>
    public const int PlayFrames = 48, PlayInterval = 8;

    private const int Kitchen = 4, Hut = 2, Store = 6, StoreSite = 16, FirstSite = 12, LastSite = 19, AdultAge = 12;
    private const int Hp = 2, MaxHp = 3, WorkMode = 4, Kind = 6, Occupants = 9, FirstOccupant = 10, Alert = 11, Aux = 14;
    private const int UnitX = 0, UnitY = 1, SubState = 13, Age = 14, Gender = 15, UnitHp = 19, Action = 28, Animation = 29;
    private const int AnimationTick = 30, CarriedItem = 35, CarriedAmount = 36, TargetIndex = 44, NextOccupant = 45;
    private const int BuildingSlot = 46, OrderMode = 61;
    private const int TakingPlace = 147, CoupleInHut = 111, Meals = 0;

    private static readonly object Gate = new();
    private static IReadOnlyList<LegacySample>? all;

    /// <summary>The working states and those played on from them.</summary>
    public static IReadOnlyList<LegacySample> All
    {
        get
        {
            lock (Gate)
            {
                return all ??= RichSamples.All.SelectMany(Busy).ToList();
            }
        }
    }

    private static IEnumerable<LegacySample> Busy(LegacySample sample)
    {
        GameCanvas game = sample.Restore();
        int variant = (sample.Frame / 1000) + (int)(sample.Seed * 5);
        PlaceStoreSite(game);
        var buildings = Enumerable.Range(1, 60).Where(b => B(game, Hp, b) > 0).OrderBy(b => Priority(game, b)).ToList();
        var free = new Queue<int>(LegacySamples.LiveUnits(game).Where(id => Free(game, id)));
        foreach (int b in buildings)
        {
            int kind = B(game, Kind, b);
            bool busy = B(game, WorkMode, b) != 0;
            bool site = kind >= FirstSite && kind <= LastSite;
            if (!(site || busy || kind == Kitchen || kind == Store || kind == Hut || kind < FirstSite))
            {
                continue;
            }
            int workers = kind == Hut ? 2 : 1 + ((b + variant) % 3);
            for (int i = 0; i < workers; i++)
            {
                int unit = TakeWorker(game, free, (b - 1) / 30, kind == Hut ? i : -1);
                if (unit > 0)
                {
                    Enter(game, unit, b, kind, busy, i + variant);
                }
            }
            Stage(game, b, kind, site, busy, b + variant);
        }
        game.sArr2_a[0][Meals] = (short)(variant % 3 == 0 ? 50 : 10);
        var states = new List<LegacySample>();
        foreach (int b in buildings.Where((_, i) => i % 5 == variant % 5).Take(3))
        {
            game.i_c = (game.i_c & ~255) + 256 + b;
            states.Add(sample with { Frame = sample.Frame + 3 + b, State = LegacySnapshot.Capture(game) });
        }
        for (int frame = 1; frame <= PlayFrames; frame++)
        {
            game.j_();
            if (frame % PlayInterval == 0)
            {
                states.Add(sample with { Frame = sample.Frame + 100 + frame, State = LegacySnapshot.Capture(game) });
            }
        }
        return states;
    }

    /// <summary>Buildings whose work matters most get workers first: stores and sites, busy buildings, kitchens, huts.</summary>
    private static int Priority(GameCanvas game, int b)
    {
        int kind = B(game, Kind, b);
        return kind == StoreSite || kind == Store ? 0
            : B(game, WorkMode, b) != 0 ? 1
            : kind >= FirstSite && kind <= LastSite ? 2
            : kind == Kitchen ? 3
            : kind == Hut ? 4
            : 5;
    }

    /// <summary>A store's construction site for the player, placed by the original.</summary>
    private static void PlaceStoreSite(GameCanvas game)
    {
        game.bArr_r[game.b_as] = Store;
        game.world_a.i_v = Store;
        for (int y = 2; y < game.s_d - 4; y += 3)
        {
            for (int x = 2; x < game.s_c / 2; x += 3)
            {
                if (game.a_BIIIIZI(0, StoreSite, x, y, 1, true, -1) > 0)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Sets a site's deliveries and hit points, an upgrade's work and the cancel flag at various stages.</summary>
    private static void Stage(GameCanvas game, int b, int kind, bool site, bool busy, int pick)
    {
        if (site)
        {
            int job = System.Array.FindIndex(game.world_a.bArr2_f[0], v => v == b);
            int target = B(game, Aux, b);
            var plan = LegacySamples.Content.Rules.Buildings.Kinds[target].Construction!;
            int trips = pick % (plan.DeliveryActions.Count + 1);
            if (job >= 0)
            {
                game.world_a.bArr2_f[1][job] = (sbyte)trips;
            }
            int hp = pick % 4 == 0 ? LegacySamples.Content.Rules.Buildings.Kinds[target].MaxHitPoints[0] : plan.ProgressHitPoints[trips] - (pick % 2);
            SetB(game, Hp, b, System.Math.Max(1, hp));
            if (pick % 4 == 0 && pick % 8 == 0)
            {
                SetB(game, Occupants, b, 0);
            }
        }
        if (busy)
        {
            int job = System.Array.FindIndex(game.world_a.bArr2_g[0], v => v == b);
            if (job >= 0)
            {
                (game.world_a.bArr2_g[1][job], game.world_a.bArr2_g[2][job]) = (pick % 3) switch
                {
                    0 => ((sbyte)0, (sbyte)0),
                    1 => ((sbyte)2, (sbyte)9),
                    _ => ((sbyte)9, (sbyte)9),
                };
            }
            if (pick % 7 == 0)
            {
                SetB(game, Alert, b, B(game, Alert, b) | 1);
            }
            if (B(game, WorkMode, b) == 2 && pick % 2 == 0)
            {
                SetB(game, Hp, b, B(game, MaxHp, b));
            }
        }
    }

    /// <summary>An adult of <paramref name="tribe"/> standing on its cell (of the gender asked for, 0 or 1, or any).</summary>
    private static int TakeWorker(GameCanvas game, Queue<int> free, int tribe, int gender)
    {
        int count = free.Count;
        for (int i = 0; i < count; i++)
        {
            int unit = free.Dequeue();
            if ((unit - 1) / 50 == tribe && (gender < 0 || U(game, Gender, unit) == gender))
            {
                return unit;
            }
            free.Enqueue(unit);
        }
        return 0;
    }

    /// <summary>Puts <paramref name="unit"/> into building <paramref name="b"/> at a work position, settled and working.</summary>
    private static void Enter(GameCanvas game, int unit, int b, int kind, bool busy, int pick)
    {
        game.bArr2_c[U(game, UnitY, unit)][U(game, UnitX, unit)] = 0;
        SetU(game, TargetIndex, unit, b);
        SetB(game, Occupants, b, B(game, Occupants, b) + 1);
        game.world_a.i_v = unit;
        game.world_a.G_();
        SetU(game, NextOccupant, unit, B(game, FirstOccupant, b));
        SetB(game, FirstOccupant, b, unit);
        SetU(game, SubState, unit, busy ? 50 : 1);
        SetU(game, Animation, unit, pick % 4 == 0 ? TakingPlace : 71);
        SetU(game, AnimationTick, unit, pick % 3 == 0 ? 0 : 30);
        SetU(game, OrderMode, unit, pick % 5 == 0 ? 3 : pick % 7 == 0 ? 2 : 0);
        int action = kind switch
        {
            Hut => CoupleInHut,
            Kitchen => U(game, BuildingSlot, unit) == 0 ? 66 : 70,
            _ when busy => pick % 2 == 0 ? 76 : 77,
            _ when kind >= FirstSite => U(game, BuildingSlot, unit) == 0 ? 78 : 56,
            Store => 40 + (pick % 4),
            _ => 59 + (pick % 2),
        };
        SetU(game, Action, unit, action);
        if (kind == Kitchen && pick % 2 == 0)
        {
            SetU(game, CarriedItem, unit, 5);
            SetU(game, CarriedAmount, unit, 3);
        }
    }

    private static bool Free(GameCanvas game, int id) =>
        U(game, Age, id) >= AdultAge && U(game, UnitHp, id) > 0 && game.bArr2_c[U(game, UnitY, id)][U(game, UnitX, id)] == id;

    private static int U(GameCanvas game, int field, int id) => game.bArr_e[UnitBase + (field * UnitStride) + id];

    private static void SetU(GameCanvas game, int field, int id, int value) => game.bArr_e[UnitBase + (field * UnitStride) + id] = (sbyte)value;

    private static int B(GameCanvas game, int field, int b) => game.bArr_e[BuildingBase + (field * BuildingStride) + b];

    private static void SetB(GameCanvas game, int field, int b, int value) => game.bArr_e[BuildingBase + (field * BuildingStride) + b] = (sbyte)value;
}
