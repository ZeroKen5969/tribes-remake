using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Map;

/// <summary>How much of a cell a tribe knows.</summary>
public enum FogState
{
    /// <summary>Seen now.</summary>
    Visible = 0,

    /// <summary>Explored before but not seen now.</summary>
    Explored = 1,

    /// <summary>Never seen.</summary>
    Unexplored = 2,
}

/// <summary>Whether units and animals can step into a cell, as route finding sees it.</summary>
internal sealed class Passability
{
    /// <summary>Movers this far away (measured, see <see cref="GridDistances"/>) or closer always block.</summary>
    private const int NearbyMeasureLimit = 29;

    /// <summary>A tribe mate standing with an animation in this range can be pushed aside.</summary>
    private const int FirstYieldingAnimation = 8;

    /// <summary>End (exclusive) of the animations a pushable tribe mate may play.</summary>
    private const int EndYieldingAnimation = 21;

    /// <summary>Animations (as signed numbers) of a unit idling inside a building; such a unit can be pushed aside.</summary>
    private const int IdleInsideAnimation = -100, IdleInsideOtherAnimation = -101;

    /// <summary>Actions during which a tribe mate playing a non-walking animation can be pushed aside.</summary>
    private const int SleepOutdoors = 79, SitDown = 80;

    private readonly GameWorld world;
    private readonly GridDistances distances;

    /// <summary>Checks cells of <paramref name="world"/>.</summary>
    public Passability(GameWorld world, GridDistances distances)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.distances = distances ?? throw new ArgumentNullException(nameof(distances));
    }

    /// <summary>
    /// Whether <paramref name="cell"/> stops <paramref name="unit"/>. Units and animals walking at
    /// least three cells away do not count (they will have moved on); the player's units plan
    /// through unexplored cells; adults walk into their enemies' traps; a unit blocked before
    /// may push past an idle tribe mate whose action has a lower move priority; a fisher may step
    /// on the shore.
    /// </summary>
    public bool BlocksUnit(Unit unit, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(unit);
        int code = CellCode.At(world.Map, cell);
        if (code == CellCode.Empty)
        {
            return false;
        }
        int tribe = unit.Tribe;
        if (IsFarMover(unit, code, cell)
            || (TribeRoles.IsPlayer(world, tribe) && !world.Fog(tribe).IsExplored(cell))
            || (IsOtherTribesTrap(code, tribe) && unit.Age >= UnitAges.Adult)
            || code == CellCode.OfUnit(unit.Id)
            || CanPushPast(unit, code))
        {
            return false;
        }
        return !(code == CellCode.Shore && unit.Action == ActionNumbers.Fishing);
    }

    /// <summary>Whether <paramref name="cell"/> stops <paramref name="animal"/>: anything but its own cell does.</summary>
    public bool BlocksAnimal(Animal animal, GridPoint cell)
    {
        ArgumentNullException.ThrowIfNull(animal);
        int code = CellCode.At(world.Map, cell);
        return code != CellCode.Empty && code != CellCode.OfAnimal(animal.Index);
    }

    /// <summary>What the original's player (<see cref="TribeRoles.Player"/>) knows of <paramref name="cell"/>.</summary>
    public FogState FogAt(GridPoint cell) => FogAt(cell, TribeRoles.Player(world));

    /// <summary>What <paramref name="tribe"/> knows of <paramref name="cell"/>.</summary>
    public FogState FogAt(GridPoint cell, int tribe)
    {
        FogOfWar fog = world.Fog(tribe);
        if (!fog.IsExplored(cell))
        {
            return FogState.Unexplored;
        }
        return fog.IsVisible(cell) ? FogState.Visible : FogState.Explored;
    }

    private bool IsFarMover(Unit unit, int code, GridPoint cell)
    {
        int mover = CellCode.UnitOf(code);
        int animal = CellCode.AnimalOf(code);
        bool moves = (mover > 0 && mover <= world.UnitSlots) || (animal >= 0 && animal < world.AnimalSlots) || code == CellCode.Reserved;
        if (!moves || distances.MeasureAtEdge(cell.X - unit.X, cell.Y - unit.Y) <= NearbyMeasureLimit)
        {
            return false;
        }
        if (code == CellCode.Reserved)
        {
            return true;
        }
        int animation = mover > 0 && mover <= world.UnitSlots ? world.Unit(mover).AnimationNumber : world.Animal(animal).Action & 0xFF;
        return animation <= AnimationNumbers.LastWalking;
    }

    private bool IsOtherTribesTrap(int code, int tribe)
    {
        int owner = CellCode.TrapOwnerOf(code);
        return owner >= 0 && TribeRoles.AreEnemies(world, owner, tribe);
    }

    private bool CanPushPast(Unit unit, int code)
    {
        int slot = CellCode.UnitOf(code);
        if (unit.PathBlock <= 0 || slot <= 0 || slot > world.UnitSlots || world.TribeOfUnit(slot) != unit.Tribe)
        {
            return false;
        }
        Unit mate = world.Unit(slot);
        var actions = world.Content.Rules.Actions.Actions;
        int ownPriority = actions[unit.Action].MovePriority;
        int matePriority = actions[mate.Action].MovePriority;
        if (ownPriority <= matePriority && matePriority != 0)
        {
            return false;
        }
        return IsYielding(mate);
    }

    private static bool IsYielding(Unit mate)
    {
        int animation = mate.Animation;
        if ((animation >= FirstYieldingAnimation && animation < EndYieldingAnimation)
            || animation == IdleInsideAnimation || animation == IdleInsideOtherAnimation)
        {
            return true;
        }
        if (mate.AnimationNumber <= AnimationNumbers.LastWalking)
        {
            return false;
        }
        int action = mate.Action;
        return action == ActionNumbers.Idle || action == SleepOutdoors || action == SitDown
            || (action >= ActionNumbers.FirstInterruption && action <= ActionNumbers.LastInterruption);
    }
}
