using System;
using Tribes.Core.Content;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>
/// Children born in huts. In the player's huts a couple's visit sets a counter that runs up to the
/// birth; the computer's huts have a child each time its birth cool-down is over (one per frame,
/// none while an intruder is about, none above its population cap). The child is born inside.
/// </summary>
internal sealed class HutBirths
{
    /// <summary>The mission without births.</summary>
    private const int NoBirthsMission = 3;

    /// <summary>A hut with more occupants than this has no room for a child.</summary>
    private const int MaxOccupants = 3;

    /// <summary>The player's hut counter: counting starts at this value; the child comes at <see cref="BirthAt"/>.</summary>
    private const int CountingFrom = 3, BirthAt = 9;

    /// <summary>The cool-down value at which the computer may have a child.</summary>
    private const int BirthReady = -1;

    /// <summary>The age of a newborn.</summary>
    private const int NewbornAge = 2;

    /// <summary>The computer's newborns get this fraction of the wave skill (1 / divisor).</summary>
    private const int NewbornSkillDivisor = 4;

    /// <summary>The newborn's first moments inside the hut.</summary>
    private const int NewbornSubState = -3, NewbornAnimation = 71;

    private readonly GameWorld world;
    private readonly UnitSpawning spawning;
    private readonly Occupancy occupancy;

    /// <summary>Has children born in <paramref name="world"/>.</summary>
    public HutBirths(GameWorld world, UnitSpawning spawning, Occupancy occupancy)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.spawning = spawning ?? throw new ArgumentNullException(nameof(spawning));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
    }

    /// <summary>The huts of <paramref name="tribe"/> for one frame (after its units).</summary>
    public void Update(int tribe)
    {
        if (world.Mission == NoBirthsMission)
        {
            return;
        }
        TribeState state = world.Tribes[tribe];
        bool computer = TribeRoles.IsComputer(world, tribe);
        if (computer && !ComputerMayHaveChild(state, tribe))
        {
            return;
        }
        ListView<Building> buildings = world.BuildingsOf(tribe);
        for (int i = 0; i < buildings.Count; i++)
        {
            Building hut = buildings[i];
            if (hut.HitPoints == 0 || hut.Kind != BuildingKindNumbers.Hut || hut.WorkMode != BuildingWorkMode.Normal
                || hut.OccupantCount > MaxOccupants)
            {
                continue;
            }
            if (!computer && hut.Aux < BirthAt)
            {
                if (hut.Aux >= CountingFrom)
                {
                    hut.Aux++;
                }
                continue;
            }
            if (computer)
            {
                state[TribeCounter.BirthCooldown] = 0;
                if (TribeRoles.Ai(world, tribe).Intruder > 0)
                {
                    return;
                }
            }
            if (!GiveBirth(tribe, hut) || computer)
            {
                return;
            }
        }
    }

    /// <summary>The computer's cool-down restarts while it is at its population cap; it may have a child once it is over.</summary>
    private bool ComputerMayHaveChild(TribeState state, int tribe)
    {
        int cap = world.Content.Rules.Missions.Missions[world.Mission].Difficulties[TribeRoles.Difficulty(world, tribe)].AiPopulationCap;
        if (state[TribeCounter.Population] >= cap)
        {
            state[TribeCounter.BirthCooldown] = 0;
        }
        return state[TribeCounter.BirthCooldown] == BirthReady;
    }

    private bool GiveBirth(int tribe, Building hut)
    {
        hut.Aux = 0;
        int skill = TribeRoles.IsComputer(world, tribe)
            ? world.Content.Rules.Missions.Missions[world.Mission].Difficulties[TribeRoles.Difficulty(world, tribe)].AttackWaveUnitSkill
                / NewbornSkillDivisor
            : 0;
        Unit? child = spawning.Spawn(tribe, new GridPoint(hut.X, hut.Y), NewbornAge, new UnitTraits(Wrap.Signed8(skill)));
        if (child is null)
        {
            return false;
        }
        child.TargetIndex = hut.Id;
        hut.OccupantCount++;
        occupancy.PickWorkPosition(child);
        child.NextOccupantOrCellObject = hut.FirstOccupant;
        hut.FirstOccupant = child.Id;
        child.ActionSubState = NewbornSubState;
        child.Animation = NewbornAnimation;
        child.AnimationTick = 0;
        child.Action = ActionNumbers.StayInside;
        return true;
    }
}
