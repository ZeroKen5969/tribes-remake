namespace Tribes.Core.Systems.Wildlife;

/// <summary>
/// The animations of wild animals. Each animal kind has its own walking, standing and attacking
/// animation, numbered from a first one by kind (kind 0 first).
/// </summary>
internal static class AnimalAnimations
{
    /// <summary>Walking animation of kind 0 (kind k walks with this + k).</summary>
    public const int FirstWalk = 2;

    /// <summary>Standing animation of kind 0.</summary>
    public const int FirstStand = 10;

    /// <summary>Attacking animation of kind 0; the bite lands when it has played to its end.</summary>
    public const int FirstAttack = 37;

    /// <summary>Animations up to this number (as unsigned) move the animal.</summary>
    public const int LastMoving = 7;

    /// <summary>The walking animation of <paramref name="kind"/>.</summary>
    public static int Walk(int kind) => FirstWalk + kind;

    /// <summary>The standing animation of <paramref name="kind"/>.</summary>
    public static int Stand(int kind) => FirstStand + kind;

    /// <summary>The attacking animation of <paramref name="kind"/>.</summary>
    public static int Attack(int kind) => FirstAttack + kind;

    /// <summary>Whether <paramref name="action"/> is one of the moving animations.</summary>
    public static bool IsMoving(int action) => (action & 0xFF) <= LastMoving;
}

/// <summary>Values of <see cref="World.Animal.ProvokeTimer"/>.</summary>
internal static class AnimalTimers
{
    /// <summary>An idle count above this (and up to <see cref="Provoked"/>) sends the animal wandering.</summary>
    public const int RestlessAfter = 5;

    /// <summary>The idle count runs up to this; above it the value is this + the slot of the unit that hit the animal.</summary>
    public const int Provoked = 19;
}

/// <summary>How much an animal is provoked (compared with <c>AnimalKind.FleeThreshold</c>).</summary>
internal static class AnimalThreats
{
    /// <summary>Seeing a unit or an occupied building of the player nearby.</summary>
    public const int Sighting = 50;

    /// <summary>Being hit.</summary>
    public const int Hit = 0;
}

/// <summary>Missions whose story changes how animals behave.</summary>
internal static class AnimalMissions
{
    /// <summary>
    /// The mission with a pen: animals of the first habitat inside it stay where they are, and they
    /// stop looking out once the story reaches <see cref="PenStoryEnd"/>.
    /// </summary>
    public const int PenMission = 3;

    /// <summary>The story stage of the pen mission from which animals no longer look out.</summary>
    public const int PenStoryEnd = 5;

    /// <summary>The story stage of the pen mission during which animals chase any distance.</summary>
    public const int PenChaseStage = 4;

    /// <summary>The mission whose herd migrates (habitat 0, then 2, then 3), and whose animals are only born late.</summary>
    public const int MigrationMission = 4;

    /// <summary>Frames before animals are born in the migration mission.</summary>
    public const int MigrationBirthDelay = 5000;

    /// <summary>The habitat the herd leaves.</summary>
    public const int HomeZone = 0;

    /// <summary>The habitat the herd sets out for.</summary>
    public const int MigratingZone = 2;

    /// <summary>The habitat the herd ends in.</summary>
    public const int MigratedZone = 3;
}
