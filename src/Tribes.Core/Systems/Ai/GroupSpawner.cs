using System;
using Tribes.Core.Content;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Ai;

/// <summary>
/// Groups of adults appearing at a place: the computer's reinforcements of an attack wave and the
/// units a story scene brings in.
/// </summary>
internal sealed class GroupSpawner
{
    /// <summary>The age of the adults that appear.</summary>
    public const int AdultAge = 20;

    /// <summary>Spiral rounds searched for a free cell (the whole map).</summary>
    public const int WholeMapRounds = 127;

    private readonly GameWorld world;
    private readonly UnitSpawning spawning;
    private readonly SpiralSearch spiral;
    private readonly StockArming arming;

    /// <summary>Brings groups into <paramref name="world"/>.</summary>
    public GroupSpawner(GameWorld world, UnitSpawning spawning, SpiralSearch spiral, StockArming arming)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.spawning = spawning ?? throw new ArgumentNullException(nameof(spawning));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
        this.arming = arming ?? throw new ArgumentNullException(nameof(arming));
    }

    /// <summary>
    /// <paramref name="count"/> adults of <paramref name="tribe"/> carrying <paramref name="weapon"/>,
    /// each on <paramref name="cell"/> or, when it is taken, on the nearest free cell. With a
    /// <paramref name="waveSkill"/> the computer's adults join its attack wave: they fight with that
    /// skill (ranged for even slots, melee for odd ones) and take a weapon from the stock.
    /// </summary>
    public void Spawn(int count, GridPoint cell, int tribe, int weapon, int? waveSkill = null)
    {
        for (int i = 0; i < count; i++)
        {
            GridPoint place = world.Map.CellAt(cell).IsEmpty ? cell : spiral.FindEmptyCell(cell, WholeMapRounds) ?? cell;
            Unit? unit = spawning.Spawn(tribe, place, AdultAge, UnitTraits.None);
            if (unit is null)
            {
                continue;
            }
            unit.Weapon = weapon;
            if (waveSkill is int skill && TribeRoles.IsComputer(world, tribe))
            {
                if ((unit.Id & 1) == 0)
                {
                    unit.RangedSkill = skill;
                }
                else
                {
                    unit.MeleeSkill = skill;
                }
                arming.Arm(unit);
                unit.Status |= UnitStatus.Mustered;
            }
        }
    }
}
