using System.Collections.Generic;
using System.Linq;
using Tribes.Legacy;
using Tribes.Trace;
using static Tribes.Parity.EntityFields;

namespace Tribes.Core.Tests.Systems;

/// <summary>
/// States in which the animals have something to do: next to people and to the player's occupied
/// buildings animals of every kind are born (with the original's own code), some of them were just
/// hit by a unit, some are about to bite. Each such state is also played on for a while by the
/// original's world update, so chases, bites, flights, deaths and carcasses follow naturally.
/// </summary>
internal static class WildlifeSamples
{
    /// <summary>Frames a provoked state is played on, and frames between two of its samples.</summary>
    public const int PlayFrames = 90, PlayInterval = 9;

    private const int Kinds = 3, AttackAnimation = 37, AdultAge = 12;
    private const int AnimalHp = 7, AnimalKind = 8, AnimalAction = 9, AnimalFrame = 10, AnimalTarget = 11;
    private const int AnimalTimer = 4, AnimalFacing = 6, AnimalDestX = 13, AnimalDestY = 14;
    private const int UnitHp = 19, UnitAge = 14, UnitX = 0, UnitY = 1;
    private const int Occupants = 9, BuildingHp = 2, BuildingX = 0, BuildingY = 1;

    private static readonly object Gate = new();
    private static IReadOnlyList<LegacySample>? all;

    /// <summary>The provoked states and the states played on from them.</summary>
    public static IReadOnlyList<LegacySample> All
    {
        get
        {
            lock (Gate)
            {
                return all ??= Bases().SelectMany(Provoke).ToList();
            }
        }
    }

    private static IEnumerable<LegacySample> Bases() =>
        RichSamples.All.Where((_, i) => i % 2 == 0).Concat(MissionSamples.All.Where((_, i) => i % 3 == 1));

    private static IEnumerable<LegacySample> Provoke(LegacySample sample)
    {
        GameCanvas game = sample.Restore();
        int kind = sample.Frame % Kinds;
        foreach (int unit in Adults(game).Take(4))
        {
            int animal = BornNear(game, Cell(game, unit), kind++ % Kinds);
            if (animal < 0)
            {
                continue;
            }
            switch (unit % 3)
            {
                case 0:
                    Set(game, AnimalTimer, animal, 19 + unit);
                    break;
                case 1:
                    AboutToBite(game, animal, unit);
                    break;
            }
        }
        foreach (int building in OccupiedPlayerBuildings(game).Take(2))
        {
            BornNear(game, (Building(game, BuildingX, building) + 1, Building(game, BuildingY, building) + 1), kind++ % Kinds);
        }
        var states = new List<LegacySample> { sample with { Frame = sample.Frame + 7, State = LegacySnapshot.Capture(game) } };
        for (int frame = 1; frame <= PlayFrames; frame++)
        {
            game.j_();
            if (frame % PlayInterval == 0)
            {
                states.Add(sample with { Frame = sample.Frame + 7 + frame, State = LegacySnapshot.Capture(game) });
            }
        }
        return states;
    }

    /// <summary>A newborn animal of <paramref name="kind"/> on a free cell next to <paramref name="cell"/>; its slot or -1.</summary>
    private static int BornNear(GameCanvas game, (int X, int Y) cell, int kind)
    {
        int free = Enumerable.Range(0, AnimalStride).FirstOrDefault(i => Animal(game, AnimalHp, i) == 0, -1);
        if (free < 0)
        {
            return -1;
        }
        for (int r = 1; r <= 2; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cell.X + dx, y = cell.Y + dy;
                    if (x > 0 && y > 0 && x < game.s_c - 1 && y < game.s_d - 1 && game.bArr2_c[y][x] == 0)
                    {
                        game.b_IIII(kind, x, y, 0);
                        return free;
                    }
                }
            }
        }
        return -1;
    }

    /// <summary>The animal faces <paramref name="unit"/>, targets it and its attack animation is about to end.</summary>
    private static void AboutToBite(GameCanvas game, int animal, int unit)
    {
        (int x, int y) = Cell(game, unit);
        int dx = System.Math.Sign(x - Animal(game, 0, animal)), dy = System.Math.Sign(y - Animal(game, 1, animal));
        int facing = LegacySamples.Content.Rules.Movement.FacingByDeltaRowMajor[(dx + 1) + ((dy + 1) * 3)];
        int kind = Animal(game, AnimalKind, animal);
        int attack = AttackAnimation + kind;
        Set(game, AnimalFacing, animal, facing);
        Set(game, AnimalTarget, animal, unit);
        Set(game, AnimalDestX, animal, x);
        Set(game, AnimalDestY, animal, y);
        Set(game, AnimalAction, animal, attack);
        Set(game, AnimalFrame, animal, LegacySamples.Content.Rules.Animations.Animations[attack].FrameCount - 1);
    }

    private static IEnumerable<int> Adults(GameCanvas game) =>
        LegacySamples.LiveUnits(game).Where(id => LegacySamples.Field(game, UnitAge, id) >= AdultAge
            && LegacySamples.Field(game, UnitHp, id) > 0 && game.bArr2_c[Cell(game, id).Y][Cell(game, id).X] == id);

    private static IEnumerable<int> OccupiedPlayerBuildings(GameCanvas game) =>
        Enumerable.Range(1, 30).Where(b => Building(game, BuildingHp, b) > 0 && Building(game, Occupants, b) > 0);

    private static (int X, int Y) Cell(GameCanvas game, int unit) =>
        (LegacySamples.Field(game, UnitX, unit), LegacySamples.Field(game, UnitY, unit));

    private static int Animal(GameCanvas game, int field, int i) => game.bArr_e[AnimalBase + (field * AnimalStride) + i];

    private static int Building(GameCanvas game, int field, int b) => game.bArr_e[BuildingBase + (field * BuildingStride) + b];

    private static void Set(GameCanvas game, int field, int i, int value) =>
        game.bArr_e[AnimalBase + (field * AnimalStride) + i] = (sbyte)value;
}
