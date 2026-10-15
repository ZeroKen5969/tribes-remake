using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Search;
using Tribes.Core.Systems.Vision;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Story;

/// <summary>
/// The moves story scenes make in the world: sending actors somewhere, gathering the player's
/// tribe at one place, and clearing the world (everything, or the enemy and the animals) for a scene
/// that sets up a new situation.
/// </summary>
internal sealed class SceneStaging
{
    /// <summary>Actor slots from this one on are the extras a scene collects; they always follow the lead.</summary>
    public const int FirstExtra = 6;

    /// <summary>Spiral rounds searched for a free cell (the whole map).</summary>
    private const int WholeMapRounds = 127;

    /// <summary>The standing animation units are left in when the tribe is gathered.</summary>
    private const int Standing = AnimationNumbers.FirstStanding;

    private readonly GameWorld world;
    private readonly UnitLifecycle lifecycle;
    private readonly BuildingLifecycle buildings;
    private readonly WildlifeAndNodes objects;
    private readonly SpiralSearch spiral;
    private readonly SightSystem sight;
    private readonly UnitActions actions;

    /// <summary>Stages scenes in <paramref name="world"/>.</summary>
    public SceneStaging(GameWorld world, UnitLifecycle lifecycle, BuildingLifecycle buildings, WildlifeAndNodes objects,
        SpiralSearch spiral, SightSystem sight, UnitActions actions)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
        this.objects = objects ?? throw new ArgumentNullException(nameof(objects));
        this.spiral = spiral ?? throw new ArgumentNullException(nameof(spiral));
        this.sight = sight ?? throw new ArgumentNullException(nameof(sight));
    }

    /// <summary>
    /// Sends the actor in slot <paramref name="lead"/> and every extra (slots from
    /// <see cref="FirstExtra"/>) walking to <paramref name="cell"/>, out of the army.
    /// </summary>
    public void SendActors(GridPoint cell, int lead)
    {
        ScriptState script = world.Script;
        for (int slot = 0; slot < ScriptState.ActorSlots; slot++)
        {
            if ((slot == lead || slot >= FirstExtra) && SceneActors.Unit(world, slot) is Unit actor)
            {
                Send(actor, cell);
            }
        }
    }

    /// <summary>
    /// Every living unit of the player's tribe is taken off its cell and put down standing on
    /// <paramref name="cell"/> or, one after the other, on the nearest free cells around it.
    /// </summary>
    public void GatherPlayerTribe(GridPoint cell)
    {
        ListView<Unit> units = world.UnitsOf(TribeRoles.Player(world));
        foreach (Unit unit in units)
        {
            if (unit.HitPoints > 0)
            {
                world.Map.SetCell(new GridPoint(unit.X, unit.Y), MapCell.Empty);
            }
        }
        GridPoint place = cell;
        foreach (Unit unit in units)
        {
            if (unit.HitPoints <= 0)
            {
                continue;
            }
            if (!world.Map.CellAt(place).IsEmpty)
            {
                place = spiral.FindEmptyCell(place, WholeMapRounds) ?? place;
            }
            ReleaseReservedCell(unit);
            PutDown(unit, place);
        }
    }

    /// <summary>
    /// <paramref name="unit"/> stops: its destination becomes its cell (the next cell when it is
    /// less than half way into it), its route is dropped, and it idles with its task site there.
    /// </summary>
    public void StopAndIdle(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        unit.DestinationX = unit.X;
        unit.DestinationY = unit.Y;
        if (IsEnteringNextCell(unit) && unit.ActionSubState >= 0 && unit.PathLength > 0)
        {
            DirectionStep step = world.Content.Rules.Movement.Directions[unit.Facing];
            unit.DestinationX += step.Dx;
            unit.DestinationY += step.Dy;
        }
        unit.PathLength = 0;
        unit.TaskSiteX = unit.DestinationX;
        unit.TaskSiteY = unit.DestinationY;
        actions.SetAction(unit, ActionNumbers.Idle);
    }

    /// <summary>Frees every building slot, takes every unit out of the world and every animal off the map.</summary>
    public void ClearWorld()
    {
        for (int id = 1; id <= world.BuildingSlots; id++)
        {
            buildings.Free(world.Building(id));
        }
        for (int id = 1; id <= world.UnitSlots; id++)
        {
            RemoveUnit(world.Unit(id));
        }
        RemoveAnimals();
    }

    /// <summary>Takes the computer's units out of the world and every animal off the map.</summary>
    public void ClearEnemyAndAnimals()
    {
        foreach (Unit unit in world.UnitsOf(TribeRoles.Computer(world)))
        {
            RemoveUnit(unit);
        }
        RemoveAnimals();
    }

    private static void Send(Unit unit, GridPoint cell)
    {
        unit.Action = ActionNumbers.Idle;
        unit.DestinationX = cell.X;
        unit.DestinationY = cell.Y;
        unit.OrderMode = OrderModes.Walk;
        unit.TargetCellCode = 0;
        unit.Status &= ~UnitStatus.Mustered;
    }

    private void PutDown(Unit unit, GridPoint place)
    {
        unit.X = unit.TaskSiteX = unit.DestinationX = place.X;
        unit.Y = unit.TaskSiteY = unit.DestinationY = place.Y;
        unit.OffsetX = unit.OffsetY = 0;
        unit.PathLength = 0;
        unit.Action = ActionNumbers.Idle;
        unit.Animation = Standing;
        unit.AnimationTick = 0;
        unit.OrderMode = OrderModes.Free;
        world.Map.SetCell(place, MapCell.UnitAt(unit.Id));
        sight.RevealDisc(TribeRoles.Player(world), FogLayer.Visible, place.X, place.Y, unit.SightRadius);
    }

    /// <summary>A unit less than half way into the next cell gives that reserved cell back.</summary>
    private void ReleaseReservedCell(Unit unit)
    {
        DirectionStep step = world.Content.Rules.Movement.Directions[unit.Facing];
        var ahead = new GridPoint(unit.X + step.Dx, unit.Y + step.Dy);
        if (IsEnteringNextCell(unit) && world.Map.Contains(ahead) && world.Map.CellAt(ahead).Content == CellContent.Reserved)
        {
            world.Map.SetCell(ahead, MapCell.Empty);
        }
    }

    /// <summary>Whether <paramref name="unit"/> is walking and less than half way into the next cell.</summary>
    private bool IsEnteringNextCell(Unit unit)
    {
        SpeedClass speed = world.Content.Rules.Movement.SpeedClasses[unit.MoveDelay - unit.RunBonus];
        int frames = ((unit.Facing & 1) == 0 ? speed.Straight : speed.Diagonal).StepFrames;
        return unit.AnimationNumber <= AnimationNumbers.LastWalking && unit.StepTicks < (frames + 1) / 2;
    }

    private void RemoveUnit(Unit unit)
    {
        if (unit.HitPoints != 0)
        {
            lifecycle.Kill(unit);
            lifecycle.FreeSlot(unit);
        }
    }

    private void RemoveAnimals()
    {
        for (int i = 0; i < world.AnimalSlots; i++)
        {
            objects.RemoveAnimal(world.Animal(i));
        }
    }
}
