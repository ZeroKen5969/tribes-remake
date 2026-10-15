using System;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Maps;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Wildlife;

/// <summary>
/// Walking animals: each frame a walking animal moves one sub-step in its facing; halfway through
/// the step it enters the next cell (the cell it leaves is cleared).
/// </summary>
internal sealed class AnimalMovement
{
    /// <summary>Width of a cell in pixels.</summary>
    public const int CellWidth = 22;

    /// <summary>Height of a cell in pixels.</summary>
    public const int CellHeight = 16;

    private readonly GameWorld world;

    /// <summary>Moves the animals of <paramref name="world"/>.</summary>
    public AnimalMovement(GameWorld world)
    {
        this.world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// One frame of <paramref name="animal"/>'s walking, if it is walking: the next sub-step's pixel
    /// offsets, and at the middle of the step the change of cell (offsets are then measured from the
    /// new cell; for an even number of sub-steps the original moves the offset by a whole cell when
    /// stepping left or up).
    /// </summary>
    public void Step(Animal animal)
    {
        ArgumentNullException.ThrowIfNull(animal);
        if (!AnimalAnimations.IsMoving(animal.Action))
        {
            return;
        }
        GameRules rules = world.Content.Rules;
        SpeedClass speed = rules.Movement.SpeedClasses[rules.Animals.Kinds[animal.Kind].SpeedClass];
        SpeedGait gait = (animal.Facing & 1) == 0 ? speed.Straight : speed.Diagonal;
        DirectionStep direction = rules.Movement.Directions[animal.Facing];
        int frames = gait.StepFrames;
        animal.StepCounter++;
        animal.OffsetX = gait.OffsetX[FrameOfStep(frames, animal.StepCounter * direction.Dx)];
        animal.OffsetY = gait.OffsetY[FrameOfStep(frames, animal.StepCounter * direction.Dy)];
        if (animal.StepCounter != (frames + 1) / 2)
        {
            return;
        }
        WorldMap map = world.Map;
        map.SetCell(new GridPoint(animal.X, animal.Y), MapCell.Empty);
        animal.X += direction.Dx;
        animal.Y += direction.Dy;
        bool evenSteps = (frames & 1) == 0;
        if (evenSteps && direction.Dx < 0)
        {
            animal.OffsetX += CellWidth;
        }
        if (evenSteps && direction.Dy < 0)
        {
            animal.OffsetY += CellHeight;
        }
        map.SetCell(new GridPoint(animal.X, animal.Y), MapCell.AnimalAt(animal.Index));
    }

    /// <summary>
    /// The frame of the offset tables for sub-step <paramref name="signedStep"/> (negative when
    /// moving left or up, which counts the frames backwards). An animal only takes as many
    /// sub-steps as a step has, after which it decides again; the result stays in the table for
    /// any count.
    /// </summary>
    private static int FrameOfStep(int frames, int signedStep) => (((frames + signedStep) % frames) + frames) % frames;
}
