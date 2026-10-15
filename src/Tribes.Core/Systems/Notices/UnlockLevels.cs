using System;
using Tribes.Core.Content.Rules;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Notices;

/// <summary>Which level of each building kind the player may build: the mission's start levels plus the unlocks due by now.</summary>
internal sealed class UnlockLevels
{
    /// <summary>A tech unlock's time is given in units of this many frames.</summary>
    public const int FramesPerTimeUnit = 100;

    private readonly GameWorld world;

    /// <summary>Keeps the unlock levels of <paramref name="world"/>.</summary>
    public UnlockLevels(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>Recomputes the highest buildable level of every building kind for the current frame.</summary>
    public void Recompute()
    {
        MissionRules mission = world.Content.Rules.Missions.Missions[world.Mission];
        for (int kind = 0; kind < GameWorld.UnlockableKinds; kind++)
        {
            world.SetUnlockLevel(kind, mission.StartUnlockLevels[kind]);
        }
        // The unlocks are listed in time order.
        foreach (TechUnlock unlock in mission.TechUnlocks)
        {
            if (world.Frame < unlock.TimeUnits * FramesPerTimeUnit)
            {
                return;
            }
            world.SetUnlockLevel(unlock.BuildingKind, world.UnlockLevel(unlock.BuildingKind) + 1);
        }
    }
}
