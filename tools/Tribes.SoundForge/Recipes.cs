using System.Collections.Generic;

namespace Tribes.SoundForge;

/// <summary>One note of a sound.</summary>
/// <param name="AtMs">When it starts, in milliseconds from the start of the sound.</param>
/// <param name="Instrument">A General MIDI program (0-based), or <see cref="Recipes.Drum"/> for the drum kit.</param>
/// <param name="Key">The MIDI key (for the drum kit: which drum).</param>
/// <param name="Velocity">How hard it is struck (1..127).</param>
/// <param name="LengthMs">How long it is held.</param>
internal sealed record Note(double AtMs, int Instrument, int Key, int Velocity, double LengthMs);

/// <summary>A sound effect: its notes and how the game plays it.</summary>
/// <param name="Name">The name the game plays it by (also the file name).</param>
/// <param name="LengthMs">Length of the rendering; the end fades out.</param>
/// <param name="Peak">Peak level the rendering is scaled to (0..1), which sets how loud it is next to the others.</param>
/// <param name="CooldownMs">Shortest time between two plays of it (more in that time are dropped).</param>
/// <param name="Notes">The notes.</param>
internal sealed record SoundRecipe(string Name, double LengthMs, double Peak, int CooldownMs, IReadOnlyList<Note> Notes);

/// <summary>
/// The sounds: wood blocks, bongos, toms, taiko drums, kalimba, flutes and a horn, instruments of
/// the original's songs or close to them, so the effects belong with the music.
/// </summary>
internal static class Recipes
{
    /// <summary>The General MIDI drum kit (channel 10) instead of a program.</summary>
    public const int Drum = -1;

    private const int Kalimba = 108, Taiko = 116, FrenchHorn = 60, PanFlute = 75, Shakuhachi = 77;
    private const int BassDrum = 36, SideStick = 37, LowFloorTom = 41, LowBongo = 61, LowConga = 64;
    private const int Cabasa = 69, ShortGuiro = 73, Claves = 75, HighWoodBlock = 76, LowWoodBlock = 77;

    public static IReadOnlyList<SoundRecipe> All { get; } =
    [
        // A button of the menus or the panels.
        new("click", 160, 0.45, 40, [new(0, Drum, HighWoodBlock, 70, 60)]),

        // A villager sent somewhere on the map, or told to attack.
        new("order", 220, 0.5, 120, [new(0, Drum, Claves, 80, 80)]),

        // A building or trap laid out.
        new("place", 420, 0.65, 150, [new(0, Drum, LowWoodBlock, 100, 90), new(10, Drum, BassDrum, 80, 120)]),

        // A blow landed on something in sight.
        new("hit", 260, 0.5, 90, [new(0, Drum, LowBongo, 115, 80), new(5, Drum, SideStick, 70, 60)]),

        // A spear, stone or axe thrown in sight.
        new("throw", 320, 0.4, 120, [new(0, Drum, Cabasa, 95, 120), new(40, Drum, ShortGuiro, 60, 80)]),

        // One of the tribe was born.
        new("birth", 1000, 0.6, 400,
        [
            new(0, Kalimba, 72, 95, 300), new(110, Kalimba, 76, 95, 300), new(220, Kalimba, 79, 100, 500),
        ]),

        // One of the tribe died.
        new("death", 1300, 0.7, 400, [new(0, Taiko, 45, 105, 400), new(260, Taiko, 40, 90, 600)]),

        // A building of the tribe was finished.
        new("built", 1100, 0.65, 400,
        [
            new(0, Drum, LowWoodBlock, 100, 90), new(140, Drum, LowWoodBlock, 100, 90),
            new(300, Kalimba, 72, 95, 500), new(300, Kalimba, 79, 85, 500),
        ]),

        // An enemy was seen.
        new("enemy", 1100, 0.6, 1500, [new(0, Shakuhachi, 69, 100, 260), new(280, Shakuhachi, 74, 105, 600)]),

        // The tribe is under attack.
        new("attack", 1500, 0.8, 2500,
        [
            new(0, Drum, LowFloorTom, 120, 100), new(130, Drum, LowFloorTom, 120, 100), new(260, Drum, LowFloorTom, 125, 100),
            new(260, FrenchHorn, 55, 110, 800),
        ]),

        // A building was destroyed.
        new("destroyed", 1400, 0.8, 600,
        [
            new(0, Taiko, 40, 120, 300), new(0, Drum, LowConga, 110, 200), new(160, Taiko, 35, 115, 700),
        ]),

        // Something cannot be done: not enough resources, or the tribe has as many as it may have.
        new("denied", 420, 0.5, 300, [new(0, Drum, LowWoodBlock, 85, 70), new(130, Drum, LowWoodBlock, 70, 70)]),

        // Another message for the tribe (a new chief to choose, a worker it needs).
        new("notice", 700, 0.45, 600, [new(0, Kalimba, 74, 80, 400)]),

        // A hint comes up.
        new("hint", 850, 0.45, 500, [new(0, Kalimba, 79, 75, 250), new(110, Kalimba, 84, 75, 450)]),

        // The match or the mission is won.
        new("victory", 2600, 0.8, 2000,
        [
            new(0, Taiko, 48, 110, 300), new(0, Kalimba, 60, 100, 300), new(160, Kalimba, 64, 100, 300),
            new(320, Kalimba, 67, 100, 300), new(480, Taiko, 48, 115, 300), new(480, Kalimba, 72, 110, 900),
            new(480, PanFlute, 72, 95, 1200),
        ]),

        // The match or the mission is lost.
        new("defeat", 2700, 0.75, 2000,
        [
            new(0, Taiko, 45, 105, 400), new(0, Shakuhachi, 62, 95, 450), new(450, Taiko, 43, 100, 400),
            new(450, Shakuhachi, 60, 95, 450), new(900, Taiko, 40, 110, 900), new(900, Shakuhachi, 57, 100, 1200),
        ]),
    ];
}
