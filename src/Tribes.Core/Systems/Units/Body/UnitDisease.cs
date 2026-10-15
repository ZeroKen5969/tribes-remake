using System;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Body;

/// <summary>
/// How units catch the disease: inside a building where someone is sick a unit's exposure grows
/// (elsewhere inside it fades), outdoors it fades, and a sick adult outdoors exposes the healthy
/// adults around it. Enough exposure makes a unit sick.
/// </summary>
internal sealed class UnitDisease
{
    /// <summary>Exposure at which a unit falls sick (and the most exposure a building gives).</summary>
    public const int SickeningExposure = 30;

    /// <summary>Exposure a sick unit gives each healthy adult next to it.</summary>
    private const int ContagionDose = 8;

    private readonly GameWorld world;

    /// <summary>Creates the system for <paramref name="world"/>.</summary>
    public UnitDisease(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Updates the exposure of healthy <paramref name="unit"/> (every second frame): outdoors it
    /// fades; inside a building it grows while someone in there is sick, else fades (tower guards
    /// are not affected). Reaching <see cref="SickeningExposure"/> makes the unit sick.
    /// </summary>
    public void UpdateExposure(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (CoupleLinks.IsOnItsCell(world, unit))
        {
            if (unit.DiseaseExposure > 0)
            {
                unit.DiseaseExposure--;
            }
        }
        else if (!UnitMood.IsTowerAction(unit.Action))
        {
            int change = InfectedBuilding(unit) ? 1 : -1;
            unit.DiseaseExposure = GridMath.Clamp(unit.DiseaseExposure + change, 0, SickeningExposure);
        }
        if (unit.DiseaseExposure >= SickeningExposure)
        {
            unit.Status |= UnitStatus.Diseased;
            unit.DiseaseExposure = 0;
        }
    }

    /// <summary>
    /// Sick <paramref name="unit"/> exposes the healthy adults on the eight cells around it (and on
    /// its own cell, which holds itself).
    /// </summary>
    public void SpreadFrom(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                var cell = new GridPoint(unit.X + dx, unit.Y + dy);
                if (!world.Map.InPlay(cell))
                {
                    continue;
                }
                MapCell content = world.Map.CellAt(cell);
                if (content.Content != CellContent.Unit)
                {
                    continue;
                }
                Unit neighbour = world.Unit(content.Index);
                if ((neighbour.Status & UnitStatus.Diseased) == 0 && neighbour.Age >= UnitAges.Adult)
                {
                    neighbour.DiseaseExposure += ContagionDose;
                }
            }
        }
    }

    private bool InfectedBuilding(Unit unit) =>
        unit.TargetIndex >= 1 && unit.TargetIndex <= world.BuildingSlots && world.Building(unit.TargetIndex).HasSickOccupant;
}
