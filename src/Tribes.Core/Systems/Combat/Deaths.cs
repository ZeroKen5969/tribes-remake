using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Map;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Story;
using Tribes.Core.Systems.WorldObjects;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Combat;

/// <summary>
/// What happens when hit points run out: an animal leaves a carcass, a unit dies (its tribe
/// mourns a dead chief and the player hears about a lost specialist), a building is destroyed.
/// A kill may also be the story step the mission waits for.
/// </summary>
internal sealed class Deaths
{
    /// <summary>Happiness the player's adults lose when their chief dies, and the happiness range.</summary>
    private const int ChiefMourning = 25, LowestHappiness = 0, HighestHappiness = 99;

    /// <summary>Death animations of adults and children, and the overlay drawn on a dead unit.</summary>
    private const int AdultDeathAnimation = 43, ChildDeathAnimation = 44, DeathOverlay = -64;

    /// <summary>Task order kind that keeps its target when the target dies, and the mark of a dead target.</summary>
    private const int TargetKeepingOrder = 3, DeadTarget = -2;

    /// <summary>Action categories of specialists whose death the player is told about, and the notices.</summary>
    private const int ArmouryWork = 8, KitchenWork = 9, DiscoWork = 10, SanctuaryWork = 11;

    private const int WeaponsmithDied = 14, CookDied = 12, DjDied = 15, WitchDoctorDied = 13;

    private readonly GameWorld world;
    private readonly WildlifeAndNodes objects;
    private readonly UnitLifecycle lifecycle;
    private readonly HintSystem hints;
    private readonly StoryProgress story;
    private readonly Action<Building, int> destroyBuilding;

    /// <summary>
    /// Handles deaths in <paramref name="world"/>; <paramref name="destroyBuilding"/> turns a
    /// building into a ruin (destroyed by a tribe, -1 for none).
    /// </summary>
    public Deaths(GameWorld world, WildlifeAndNodes objects, UnitLifecycle lifecycle, HintSystem hints,
        StoryProgress story, Action<Building, int> destroyBuilding)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
        this.objects = objects ?? throw new ArgumentNullException(nameof(objects));
        this.lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        this.hints = hints ?? throw new ArgumentNullException(nameof(hints));
        this.story = story ?? throw new ArgumentNullException(nameof(story));
        this.destroyBuilding = destroyBuilding ?? throw new ArgumentNullException(nameof(destroyBuilding));
    }

    /// <summary>
    /// The entity in slot <paramref name="slot"/> whose hit points (<paramref name="hitPoints"/>
    /// says whose kind) ran out at <paramref name="cell"/> dies. A dying unit that the units' update
    /// has still to reach this frame (<paramref name="updatedLaterThisFrame"/>) starts its death
    /// animation one frame later, so that every death lasts as long. <paramref name="killer"/> is the
    /// tribe that dealt the blow (-1 for none: a trap, hunger, an animal).
    /// </summary>
    public void Die(EntityProperty hitPoints, GridPoint cell, int slot, bool updatedLaterThisFrame, int killer = -1)
    {
        switch (hitPoints)
        {
            case EntityProperty.AnimalHitPoints:
                AnimalDies(world.Animal(slot), cell);
                break;
            case EntityProperty.UnitHitPoints:
                UnitDies(world.Unit(slot), cell, updatedLaterThisFrame, killer);
                break;
            case EntityProperty.BuildingHitPoints:
                destroyBuilding(world.Building(slot), killer);
                break;
        }
        bool storyKill = hitPoints == EntityProperty.AnimalHitPoints
            || (hitPoints == EntityProperty.UnitHitPoints && !TribeRoles.IsPlayer(world, world.TribeOfUnit(slot)));
        if (storyKill && story.NextStep is { Trigger: StoryProgress.KillTrigger } step)
        {
            story.CastActors(0, 0, world.FirstUnitSlot(TribeRoles.Player(world)));
            story.Reach(step);
        }
    }

    private void AnimalDies(Animal animal, GridPoint cell)
    {
        int kind = animal.Kind;
        objects.RemoveAnimal(animal);
        objects.PlaceNode(NodeKinds.Carcass, cell, world.Content.Rules.Animals.Kinds[kind].CarcassAmount);
    }

    private void UnitDies(Unit victim, GridPoint cell, bool updatedLaterThisFrame, int killer)
    {
        if (TribeRoles.IsPlayer(world, victim.Tribe))
        {
            if (SpecialistNotice(victim) is { } notice)
            {
                hints.Notice(victim.Tribe, notice, cell.X, cell.Y, victim.Id);
            }
            if (victim.Id == world.Tribes[victim.Tribe][TribeCounter.Chief])
            {
                Mourn(victim.Tribe);
            }
        }
        lifecycle.Kill(victim, killer);
        victim.Animation = victim.Age >= UnitAges.Adult ? AdultDeathAnimation : ChildDeathAnimation;
        victim.AnimationTick = updatedLaterThisFrame ? -1 : 0;
        victim.BuildingSlot = DeathOverlay;
        foreach (TaskOrder order in world.Tribes[victim.Tribe].TaskOrders)
        {
            if (order.Building == 0)
            {
                break;
            }
            if (order.A == victim.Id && order.Kind != TargetKeepingOrder)
            {
                order.A = DeadTarget;
            }
        }
    }

    private int? SpecialistNotice(Unit victim)
    {
        GameRules rules = world.Content.Rules;
        return rules.Actions.Actions[victim.AssignedTask].Category switch
        {
            ArmouryWork => WeaponsmithDied,
            KitchenWork => CookDied,
            DiscoWork => DjDied,
            SanctuaryWork => WitchDoctorDied,
            _ => null,
        };
    }

    private void Mourn(int tribe)
    {
        foreach (Unit unit in world.UnitsOf(tribe))
        {
            if (unit.Age >= UnitAges.Adult)
            {
                unit.Happiness = GridMath.Clamp(unit.Happiness - ChiefMourning, LowestHappiness, HighestHappiness);
            }
        }
    }
}
