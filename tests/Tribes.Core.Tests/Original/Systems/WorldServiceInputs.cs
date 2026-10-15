using System.Collections.Generic;
using System.Linq;
using Tribes.Core.Content;
using Tribes.Legacy;
using static Tribes.Parity.EntityFields;

namespace Tribes.Core.Tests.Systems;

/// <summary>Inputs for the parity tests of the world services, picked from a sampled original state.</summary>
internal static class WorldServiceInputs
{
    /// <summary>Unit field numbers of the original read by the tests.</summary>
    public const int UnitX = 0, UnitY = 1, UnitTargetIndex = 44, UnitAction = 28, UnitDestinationX = 31, UnitDestinationY = 32;

    /// <summary>Animal field numbers of the original read by the tests.</summary>
    public const int AnimalX = 0, AnimalY = 1, AnimalHitPoints = 7, AnimalDestinationX = 13, AnimalDestinationY = 14;

    /// <summary>The original's grid size.</summary>
    public const int Grid = 96;

    /// <summary>Animal field <paramref name="field"/> of animal <paramref name="index"/>.</summary>
    public static int AnimalField(GameCanvas game, int field, int index) =>
        game.bArr_e[AnimalBase + (field * AnimalStride) + index];

    /// <summary>Node field <paramref name="field"/> of node <paramref name="index"/>.</summary>
    public static int NodeField(GameCanvas game, int field, int index) =>
        game.bArr_e[NodeBase + (field * NodeStride) + index];

    /// <summary>The live animals of the sampled state.</summary>
    public static IEnumerable<int> LiveAnimals(GameCanvas game) =>
        Enumerable.Range(0, AnimalStride).Where(i => AnimalField(game, AnimalHitPoints, i) > 0);

    /// <summary>The tribe of unit slot <paramref name="id"/>.</summary>
    public static int TribeOf(int id) => (id - 1) / 50;

    /// <summary>The cell code at (<paramref name="x"/>, <paramref name="y"/>) in the original.</summary>
    public static int CodeAt(GameCanvas game, int x, int y) => game.bArr2_c[y][x];

    /// <summary>Whether the cell is in play in the sampled state.</summary>
    public static bool InPlay(GameCanvas game, int x, int y) => x >= 0 && y >= 0 && x < game.s_c && y < game.s_d;

    /// <summary>
    /// Cells around (<paramref name="x"/>, <paramref name="y"/>) within <paramref name="radius"/>,
    /// every <paramref name="stride"/>-th one, kept in play.
    /// </summary>
    public static IEnumerable<GridPoint> Around(GameCanvas game, int x, int y, int radius, int stride = 1)
    {
        int i = 0;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (i++ % stride == 0 && InPlay(game, x + dx, y + dy))
                {
                    yield return new GridPoint(x + dx, y + dy);
                }
            }
        }
    }

    /// <summary>The position of unit <paramref name="id"/>.</summary>
    public static GridPoint UnitCell(GameCanvas game, int id) =>
        new(LegacySamples.Field(game, UnitX, id), LegacySamples.Field(game, UnitY, id));

    /// <summary>The destination of unit <paramref name="id"/>.</summary>
    public static GridPoint UnitDestination(GameCanvas game, int id) =>
        new(LegacySamples.Field(game, UnitDestinationX, id), LegacySamples.Field(game, UnitDestinationY, id));
}
