using System;
using System.Globalization;

namespace Tribes.Client.Ui.Logic;

/// <summary>What a save's one-line summary of its world settings says, before it is put into words.</summary>
/// <param name="DifficultyKey">Text key of the computers' difficulty.</param>
/// <param name="GraceMinutes">The opening grace in whole minutes (0: none).</param>
/// <param name="Stock">The start stock as a multiple of the map's own ("1", "2", "1.5").</param>
/// <param name="WildAnimals">Whether wild animals live in the world.</param>
public sealed record SaveSettingsParts(string DifficultyKey, int GraceMinutes, string Stock, bool WildAnimals);

/// <summary>
/// The world settings a save of a match or endless world was started with, as the load screens
/// list them: the computers' difficulty, the opening grace, the start stock and the wild animals.
/// </summary>
public static class SaveSettingsLine
{
    /// <summary>The map's own start stock, in percent.</summary>
    public const int OriginalStockPercent = 100;

    /// <summary>
    /// The parts for a save of computer <paramref name="difficulty"/> whose world has
    /// <paramref name="graceFrames"/> of opening grace, <paramref name="stockPercent"/> of the map's
    /// stock and, or not, <paramref name="wildAnimals"/>.
    /// </summary>
    public static SaveSettingsParts For(int difficulty, int graceFrames, int stockPercent, bool wildAnimals, int framesPerMinute)
    {
        if (framesPerMinute <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerMinute), framesPerMinute, "a minute has frames");
        }
        string stock = (Math.Max(0, stockPercent) / (double)OriginalStockPercent).ToString("0.##", CultureInfo.InvariantCulture);
        return new SaveSettingsParts(difficulty > 0 ? "difficulty.hard" : "difficulty.easy", Math.Max(0, graceFrames) / framesPerMinute, stock, wildAnimals);
    }
}
