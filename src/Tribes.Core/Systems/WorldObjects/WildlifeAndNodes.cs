using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.WorldObjects;

/// <summary>Resource nodes and wild animals the world places and removes.</summary>
internal sealed class WildlifeAndNodes
{
    /// <summary>The draw that picks a newborn animal's facing.</summary>
    private const int FacingMask = 255, Facings = 8;

    private readonly GameWorld world;

    /// <summary>Places and removes objects of <paramref name="world"/>.</summary>
    public WildlifeAndNodes(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Puts a resource node of <paramref name="kind"/> holding <paramref name="amount"/> on
    /// <paramref name="cell"/>, in the first free slot. When every slot is taken, the first
    /// carcass or bones node gives up its slot (its cell is cleared); without one nothing is
    /// placed and null is returned.
    /// </summary>
    public ResourceNode? PlaceNode(int kind, GridPoint cell, int amount)
    {
        WorldMap map = world.Map;
        ResourceNode? node = map.PlaceResourceNode(kind, cell, amount);
        if (node != null)
        {
            return node;
        }
        foreach (ResourceNode spent in map.ResourceNodes)
        {
            if (NodeKinds.IsRemains(spent.Kind))
            {
                map.SetCell(spent.Position, MapCell.Empty);
                spent.Kind = kind;
                spent.X = cell.X;
                spent.Y = cell.Y;
                spent.Amount = amount;
                map.SetCell(cell, MapCell.Node(spent.Index));
                return spent;
            }
        }
        return null;
    }

    /// <summary>
    /// A newborn animal of <paramref name="kind"/> on <paramref name="cell"/>, belonging to spawn
    /// zone <paramref name="zone"/>, in the first free slot (counted in its zone except in one
    /// mission); null when every slot is taken.
    /// </summary>
    public Animal? SpawnAnimal(int kind, GridPoint cell, int zone)
    {
        Animal? animal = FirstFreeAnimal() ?? (world.Map.IsChunked ? world.AddAnimalSlot() : null);
        if (animal == null)
        {
            return null;
        }
        // The war mission's animals are not counted against their spawn zone.
        if (world.Mission != MissionNumbers.War)
        {
            world.SetAnimalZoneCount(zone, world.AnimalZoneCount(zone) + 1);
        }
        AnimalKind animalKind = world.Content.Rules.Animals.Kinds[kind];
        animal.HitPoints = animalKind.MaxHitPoints;
        animal.Kind = kind;
        animal.X = animal.DestinationX = cell.X;
        animal.Y = animal.DestinationY = cell.Y;
        animal.OffsetX = animal.OffsetY = 0;
        animal.PathIndex = animal.PathLength = 0;
        animal.Target = 0;
        animal.Facing = world.Random.NextMasked(FacingMask, Facings);
        animal.SpawnZone = zone;
        animal.Action = animalKind.IdleAnimations[0];
        animal.ActionFrame = 0;
        animal.ProvokeTimer = 0;
        world.Map.SetCell(cell, MapCell.AnimalAt(animal.Index));
        return animal;
    }

    /// <summary>
    /// Takes a dead animal off the map: its cell and, if it was stepping into the next cell, that
    /// reserved cell are cleared, the slot is freed and its spawn zone has one animal less.
    /// </summary>
    public void RemoveAnimal(Animal animal)
    {
        ArgumentNullException.ThrowIfNull(animal);
        WorldMap map = world.Map;
        GameRules rules = world.Content.Rules;
        if ((animal.Action & 0xFF) <= AnimationNumbers.LastWalking && animal.StepCounter < HalfStep(animal, rules))
        {
            DirectionStep step = rules.Movement.Directions[animal.Facing];
            var ahead = new GridPoint(animal.X + step.Dx, animal.Y + step.Dy);
            if (map.Contains(ahead) && map.CellAt(ahead).Content == CellContent.Reserved)
            {
                map.SetCell(ahead, MapCell.Empty);
            }
        }
        map.SetCell(new GridPoint(animal.X, animal.Y), MapCell.Empty);
        animal.HitPoints = 0;
        int zone = animal.SpawnZone;
        if (zone < rules.Missions.Missions[world.Mission].ActiveAnimalSpawnPoints)
        {
            world.SetAnimalZoneCount(zone, world.AnimalZoneCount(zone) - 1);
        }
    }

    private static int HalfStep(Animal animal, GameRules rules)
    {
        SpeedClass speed = rules.Movement.SpeedClasses[rules.Animals.Kinds[animal.Kind].SpeedClass];
        SpeedGait gait = (animal.Facing & 1) == 0 ? speed.Straight : speed.Diagonal;
        return (gait.StepFrames + 1) / 2;
    }

    private Animal? FirstFreeAnimal()
    {
        for (int i = 0; i < world.AnimalSlots; i++)
        {
            if (!world.Animal(i).InUse)
            {
                return world.Animal(i);
            }
        }
        return null;
    }
}
