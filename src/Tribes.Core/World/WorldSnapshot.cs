using System;
using System.IO;
using System.Text;
using Tribes.Core.Content;
using Tribes.Core.Maps;

namespace Tribes.Core.World;

/// <summary>
/// The whole rule state of a world as bytes: for saved games, for sending a game to a player who
/// joins, and as a hash for checking that every player's game is the same.
/// </summary>
public static class WorldSnapshot
{
    private const int Magic = 0x57425254; // "TRBW"
    private const int FirstFormat = 1;

    /// <summary>The kinds of map a saved world has (format 3 on).</summary>
    private const int FixedMap = 0, ChunkedMap = 1;

    /// <summary>Writes <paramref name="world"/> to <paramref name="output"/>.</summary>
    public static void Save(GameWorld world, Stream output)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(output);
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(StateExchange.CurrentFormat);
        WorldLayout layout = world.Layout;
        bool chunked = world.Map.IsChunked;
        foreach (int value in new[]
        {
            layout.Tribes, layout.UnitsPerTribe, layout.BuildingsPerTribe, layout.Animals,
            layout.FogSize.Width, layout.FogSize.Height,
            chunked ? 0 : world.Map.Size.Width, chunked ? 0 : world.Map.Size.Height, chunked ? 0 : world.Map.ResourceNodes.Count,
            chunked ? ChunkedMap : FixedMap,
        })
        {
            writer.Write(value);
        }
        world.Exchange(new StateWriter(writer));
    }

    /// <summary>
    /// The world written by <see cref="Save"/> (this format or any older one, back to the original
    /// two-tribe format 1); <paramref name="content"/> must be the game's content.
    /// </summary>
    public static GameWorld Load(Stream input, GameContent content)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(content);
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Magic)
        {
            throw new InvalidDataException("not a saved world");
        }
        int version = reader.ReadInt32();
        if (version < FirstFormat || version > StateExchange.CurrentFormat)
        {
            throw new InvalidDataException($"saved world format {version} is not supported");
        }
        var layout = new WorldLayout(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
            new GridSize(reader.ReadInt32(), reader.ReadInt32()));
        var size = new GridSize(reader.ReadInt32(), reader.ReadInt32());
        int nodes = reader.ReadInt32();
        int kind = version >= StateExchange.EndlessFormat ? reader.ReadInt32() : FixedMap;
        WorldMap map = kind switch
        {
            FixedMap => new WorldMap(size, size, nodes),
            ChunkedMap => WorldMap.Chunked(),
            _ => throw new InvalidDataException($"saved world: unknown map kind {kind}"),
        };
        if (kind == ChunkedMap)
        {
            // An endless world plays with the endless content (its own building kinds).
            content = Systems.Endless.EndlessContent.For(content);
        }
        var world = new GameWorld(content, map, layout, new GameRandom(0));
        world.Exchange(new StateReader(reader) { Format = version });
        if (version < StateExchange.SettingsFormat && world.Endless != null)
        {
            // An endless world saved before settings were kept had the endless rules' opening grace.
            world.Settings = world.Settings with { OpeningGraceFrames = content.Endless!.Pacing.OpeningGraceFrames };
        }
        return world;
    }

    /// <summary>A hash of every value of <paramref name="world"/>; equal worlds have equal hashes.</summary>
    public static ulong Hash(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var hasher = new StateHasher();
        world.Exchange(hasher);
        return hasher.Hash;
    }
}
