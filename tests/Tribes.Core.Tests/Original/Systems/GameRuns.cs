using System.Linq;
using Tribes.Legacy;
using static Tribes.Parity.EntityFields;

namespace Tribes.Core.Tests.Systems;

/// <summary>Helpers for parity checks made while a key-monkey game runs.</summary>
internal static class GameRuns
{
    private const int UnitHitPointsField = 19, BuildingHitPointsField = 2;

    /// <summary>Whether a mission is being played: the world exists and some unit and some building are alive.</summary>
    public static bool InPlay(GameCanvas game)
    {
        if (game.world_a == null)
        {
            return false;
        }
        sbyte[] state = game.bArr_e;
        return Enumerable.Range(1, 100).Any(id => state[UnitBase + (UnitHitPointsField * UnitStride) + id] > 0)
            && Enumerable.Range(1, 60).Any(id => state[BuildingBase + (BuildingHitPointsField * BuildingStride) + id] > 0);
    }
}
