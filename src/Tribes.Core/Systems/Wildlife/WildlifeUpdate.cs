using System;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Wildlife;

/// <summary>
/// The wildlife part of a frame: a new animal may be born, then every living animal, in slot
/// order, decides and moves.
/// </summary>
internal sealed class WildlifeUpdate
{
    private readonly GameWorld world;
    private readonly AnimalSpawning spawning;
    private readonly AnimalBehaviour behaviour;
    private readonly AnimalMovement movement;

    /// <summary>Updates the animals of <paramref name="world"/>.</summary>
    public WildlifeUpdate(GameWorld world, AnimalSpawning spawning, AnimalBehaviour behaviour, AnimalMovement movement)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.spawning = spawning ?? throw new ArgumentNullException(nameof(spawning));
        this.behaviour = behaviour ?? throw new ArgumentNullException(nameof(behaviour));
        this.movement = movement ?? throw new ArgumentNullException(nameof(movement));
    }

    /// <summary>Runs the wildlife part of the current frame.</summary>
    public void RunFrame()
    {
        spawning.SpawnIfDue();
        for (int i = 0; i < world.AnimalSlots; i++)
        {
            Animal animal = world.Animal(i);
            if (animal.HitPoints > 0)
            {
                behaviour.Update(animal);
                movement.Step(animal);
            }
        }
    }
}
