using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Missions;

/// <summary>
/// Clears a world for a mission to start on it: every unit, building, animal and resource node,
/// the map, the explored fog, the work lists and orders, the counters (unless a story scene is
/// playing, which keeps the tribe's stock), the objectives and the computer player.
/// Some tables are only cleared in the column that marks a slot as used (the couple state, the
/// building of a construction site, an upgrade or a dust cloud); their other columns keep their
/// values until the slot is used again, as in the original.
/// </summary>
internal sealed class MissionReset
{
    /// <summary>The value an unused unit's entrance field is set to.</summary>
    public const int NoEntrance = -100;

    /// <summary>The living space the player starts with before any hut stands.</summary>
    public const int StartLivingSpace = 1;

    /// <summary>The mission whose units start working on their own (both tribes) and whose wave flag starts set.</summary>
    private const int VillageMission = 2;

    /// <summary>The other mission whose computer flags start with the high bit set.</summary>
    private const int AmbushMission = 4;

    /// <summary>Both tribes' "picks work by itself" bits.</summary>
    private const int BothTribesWorkFreely = 3;

    /// <summary>The computer flag set at the start of the village and ambush missions.</summary>
    private const int StartWaveFlag = unchecked((sbyte)0x80);

    /// <summary>Frames without fighting a mission starts with (a quiet start).</summary>
    private const int QuietStart = 50;

    private readonly GameWorld world;
    private readonly UnlockLevels unlocks;

    /// <summary>Resets <paramref name="world"/>.</summary>
    public MissionReset(GameWorld world, UnlockLevels unlocks)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.unlocks = unlocks ?? throw new ArgumentNullException(nameof(unlocks));
    }

    /// <summary>Clears the world for the mission set in <see cref="GameWorld.Mission"/>.</summary>
    public void Reset()
    {
        ClearEntities();
        ClearTribeTables();
        ClearWorldTables();
        ClearMap();
        if (!world.Script.Active)
        {
            foreach (TribeState tribe in world.Tribes)
            {
                for (int slot = 0; slot < TribeState.CounterCount; slot++)
                {
                    tribe.SetCounter(slot, 0);
                }
            }
        }
        foreach (TribeState tribe in world.Tribes)
        {
            if (TribeRoles.IsPlayer(world, tribe.Index))
            {
                tribe[TribeCounter.LivingSpace] = StartLivingSpace;
            }
            tribe.Army.Rally = false;
            tribe.Army.AllOut = false;
        }
        world.Frame = 0;
        world.RowSweep = 0;
        world.Script.AwaitingNewChief = false;
        world.EndCountdown = 0;
        ResetObjectivesAndComputer();
    }

    /// <summary>Every field of every unit, building, animal and resource node becomes 0 (routes are kept).</summary>
    private void ClearEntities()
    {
        foreach (EntityProperty property in Enum.GetValues<EntityProperty>())
        {
            if (!EntityProperties.IsHeldByModel(property) || EntityProperties.IsConstant(property))
            {
                continue;
            }
            (int first, int last) = SlotRange(EntityProperties.EntityOf(property));
            for (int slot = first; slot <= last; slot++)
            {
                EntityProperties.Set(world, property, slot, 0);
            }
        }
        for (int id = 1; id <= world.UnitSlots; id++)
        {
            world.Unit(id).EntranceFacingOrOrder = NoEntrance;
        }
    }

    private (int First, int Last) SlotRange(EntityKind kind) => kind switch
    {
        EntityKind.Unit => (1, world.UnitSlots),
        EntityKind.Building => (1, world.BuildingSlots),
        EntityKind.Animal => (0, world.AnimalSlots - 1),
        _ => (0, world.Map.ResourceNodes.Count - 1),
    };

    /// <summary>Armoury queues, task orders and alarms of every tribe.</summary>
    private void ClearTribeTables()
    {
        foreach (TribeState tribe in world.Tribes)
        {
            foreach (ArmouryOrders queue in tribe.Armouries)
            {
                for (int product = 0; product < ArmouryOrders.ProductCount; product++)
                {
                    queue.SetPending(product, 0);
                }
                queue.Current = 0;
                queue.Armoury = 0;
            }
            foreach (TaskOrder order in tribe.TaskOrders)
            {
                (order.Building, order.Kind, order.A, order.B, order.Extra) = (0, 0, 0, 0, 0);
            }
            foreach (AlarmCall alarm in tribe.Alarms)
            {
                alarm.Age = -1;
            }
        }
    }

    /// <summary>Traps, dust clouds, couples, construction sites, upgrades and animal zones.</summary>
    private void ClearWorldTables()
    {
        foreach (TrapOrder trap in world.Traps)
        {
            (trap.OwnerBuilding, trap.Kind, trap.X, trap.Y) = (0, 0, 0, 0);
        }
        foreach (DustCloud cloud in world.Dust)
        {
            cloud.Building = 0;
        }
        foreach (Couple couple in world.Couples)
        {
            couple.State = 0;
        }
        foreach (ConstructionJob job in world.Construction)
        {
            job.Building = 0;
        }
        foreach (UpgradeJob job in world.Upgrades)
        {
            job.Building = 0;
        }
        int zones = world.Endless?.Habitats.Count ?? GameWorld.AnimalZones;
        for (int zone = 0; zone < zones; zone++)
        {
            world.SetAnimalZoneCount(zone, 0);
        }
    }

    /// <summary>Empties every cell, sets every tile to 0 and forgets what the tribes explored.</summary>
    private void ClearMap()
    {
        world.Map.Clear();
        foreach (TribeState tribe in world.Tribes)
        {
            world.Fog(tribe.Index).ClearExplored();
        }
    }

    /// <summary>
    /// Objectives, tutorial, timed unlocks, the hint channels of the human-ruled tribes and the
    /// computer player state of the computer tribes for the new mission.
    /// </summary>
    private void ResetObjectivesAndComputer()
    {
        ScriptState script = world.Script;
        script.ObjectiveStage = 0;
        script.TutorialObjective = 0;
        script.ObjectiveTimer = 0;
        script.TutorialTimer = 0;
        script.UnlocksFired = 0;
        script.BehaviourMask = world.Mission == VillageMission ? BothTribesWorkFreely : 0;
        world.FramesSinceCombat = QuietStart;
        foreach (TribeState tribe in world.Tribes)
        {
            tribe.FramesSinceCombat = QuietStart;
            if (tribe.Controller.RunsAi)
            {
                ResetComputer(tribe.Ai);
            }
            else
            {
                for (int channel = 0; channel < HintState.Channels; channel++)
                {
                    tribe.Hints.SetCooldown(channel, HintState.Free);
                }
            }
        }
        unlocks.Recompute();
    }

    /// <summary>The computer player state a mission starts with (also for a tribe joining later).</summary>
    public void ResetComputer(AiState ai)
    {
        ArgumentNullException.ThrowIfNull(ai);
        ai.WaveTarget = -1;
        ai.NextWaveFrame = -1;
        ai.WaveStartFrame = -1;
        ai.Flags = world.Mission is VillageMission or AmbushMission ? StartWaveFlag : 0;
        ai.RebuildSlot = -1;
        ai.Intruder = -1;
        for (int slot = 0; slot < AiState.JobSlotCount; slot++)
        {
            ai.SetJobSlot(slot, slot);
        }
    }
}
