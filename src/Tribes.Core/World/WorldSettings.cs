using System;
using System.IO;

namespace Tribes.Core.World;

/// <summary>
/// Choices a match or an endless world was started with that the rules keep reading (the lobby's
/// settings); saved with the world, so a resumed or joined game plays by the same ones. The
/// campaign has none (<see cref="Original"/>).
/// </summary>
public sealed record WorldSettings
{
    /// <summary>Start stock in percent of the map's own (100 plays as the original).</summary>
    public const int OriginalStock = 100;

    /// <summary>The highest start stock in percent.</summary>
    public const int MostStock = 1000;

    /// <summary>The settings of a world that plays the original's rules: no grace, the map's stock, wild animals.</summary>
    public static WorldSettings Original { get; } = new();

    /// <summary>
    /// No computer tribe attacks anyone before this frame of the world: no attack waves, no chasing
    /// of intruders on its land; its ramp-up counts from then on. 0 for none.
    /// </summary>
    public int OpeningGraceFrames { get; init; }

    /// <summary>Every tribe's start stock in percent of the map's own (<see cref="OriginalStock"/>).</summary>
    public int StartStockPercent { get; init; } = OriginalStock;

    /// <summary>Whether wild animals live in the world (none are placed or born when off).</summary>
    public bool WildAnimals { get; init; } = true;

    /// <summary>Why these settings cannot start a world, or null when they can.</summary>
    public string? Problem()
    {
        if (OpeningGraceFrames < 0)
        {
            return "the opening grace cannot be negative";
        }
        return StartStockPercent is < 0 or > MostStock ? $"the start stock is 0..{MostStock} percent" : null;
    }

    /// <summary>Saves, loads or hashes <paramref name="settings"/> (format 4 on).</summary>
    /// <exception cref="InvalidDataException">A loaded value is out of range.</exception>
    internal static WorldSettings Exchange(StateExchange s, WorldSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var exchanged = new WorldSettings
        {
            OpeningGraceFrames = s.Value(settings.OpeningGraceFrames),
            StartStockPercent = s.Value(settings.StartStockPercent),
            WildAnimals = s.Flag(settings.WildAnimals),
        };
        return exchanged.Problem() is string problem ? throw new InvalidDataException("world settings: " + problem) : exchanged;
    }
}
