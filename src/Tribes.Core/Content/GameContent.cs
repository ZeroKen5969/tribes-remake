using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Tribes.Core.Content.Rules;

namespace Tribes.Core.Content;

/// <summary>
/// The game's content: scenarios, maps, terrain, resource nodes and the rule tables. It lives as
/// JSON files in one directory (tools/Tribes.DataExport writes them from the original game's
/// data), so it can be read and edited without the original's binary formats.
/// </summary>
public sealed class GameContent
{
    private const string MissionsFile = "missions.json";
    private const string TerrainFile = "terrain.json";
    private const string ResourceNodesFile = "resource-nodes.json";
    private const string MapsDirectory = "maps";
    private const string RulesDirectory = "rules";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Creates content from its parts, without rule tables (<see cref="GameRules.Empty"/>).</summary>
    public GameContent(IEnumerable<MissionDefinition> missions, IEnumerable<MapDefinition> maps,
        TerrainCatalog terrain, IEnumerable<ResourceNodeKind> resourceNodeKinds)
        : this(missions, maps, terrain, resourceNodeKinds, GameRules.Empty)
    {
    }

    /// <summary>Creates content from its parts.</summary>
    /// <exception cref="InvalidDataException">A mission refers to a missing map or the rules to a missing mission.</exception>
    public GameContent(IEnumerable<MissionDefinition> missions, IEnumerable<MapDefinition> maps,
        TerrainCatalog terrain, IEnumerable<ResourceNodeKind> resourceNodeKinds, GameRules rules)
    {
        Missions = missions.ToDictionary(m => m.Id);
        Maps = maps.ToDictionary(m => m.Id);
        Terrain = terrain;
        ResourceNodeKinds = resourceNodeKinds.ToDictionary(k => k.Id);
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        foreach (MissionDefinition mission in Missions.Values)
        {
            if (!Maps.ContainsKey(mission.MapId))
            {
                throw new InvalidDataException($"mission {mission.Id} uses missing map {mission.MapId}");
            }
        }
        foreach (int mission in rules.Missions.Missions.Select(m => m.Id).Concat(rules.Missions.MenuMissions))
        {
            if (!Missions.ContainsKey(mission))
            {
                throw new InvalidDataException($"the rules refer to missing mission {mission}");
            }
        }
    }

    /// <summary>Scenarios by number.</summary>
    public IReadOnlyDictionary<int, MissionDefinition> Missions { get; }

    /// <summary>Maps by number.</summary>
    public IReadOnlyDictionary<int, MapDefinition> Maps { get; }

    /// <summary>The terrain tiles.</summary>
    public TerrainCatalog Terrain { get; }

    /// <summary>Resource node kinds by number.</summary>
    public IReadOnlyDictionary<int, ResourceNodeKind> ResourceNodeKinds { get; }

    /// <summary>The rule tables (the rules/ directory).</summary>
    public GameRules Rules { get; }

    /// <summary>The rules of endless worlds (<c>endless/endless.json</c>), or null when the content has none.</summary>
    public EndlessRules? Endless { get; init; }

    /// <summary>Reads the content files in <paramref name="directory"/>.</summary>
    public static GameContent Load(string directory)
    {
        var missions = Read<MissionsJson>(Path.Combine(directory, MissionsFile)).Missions;
        var terrain = Read<TerrainJson>(Path.Combine(directory, TerrainFile));
        var kinds = Read<ResourceNodesJson>(Path.Combine(directory, ResourceNodesFile)).Kinds;
        var maps = Directory.GetFiles(Path.Combine(directory, MapsDirectory), "map-*.json")
            .Select(f => Read<MapJson>(f).ToMap());
        var rules = GameRules.Load(Path.Combine(directory, RulesDirectory));
        return new GameContent(missions, maps, terrain.ToCatalog(), kinds, rules) { Endless = EndlessRules.Load(directory) };
    }

    /// <summary>Writes the content files to <paramref name="directory"/>.</summary>
    public void Save(string directory)
    {
        Directory.CreateDirectory(Path.Combine(directory, MapsDirectory));
        Write(Path.Combine(directory, MissionsFile),
            new MissionsJson(Missions.Values.OrderBy(m => m.Id).ToArray()));
        Write(Path.Combine(directory, TerrainFile), TerrainJson.From(Terrain));
        Write(Path.Combine(directory, ResourceNodesFile),
            new ResourceNodesJson(ResourceNodeKinds.Values.OrderBy(k => k.Id).ToArray()));
        foreach (MapDefinition map in Maps.Values)
        {
            Write(Path.Combine(directory, MapsDirectory,
                    string.Create(CultureInfo.InvariantCulture, $"map-{map.Id}.json")),
                MapJson.From(map));
        }
        Rules.Save(Path.Combine(directory, RulesDirectory));
        Endless?.Save(directory);
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"{path} is empty");

    // Line ends are fixed so that the files are the same whatever system writes them.
    private static void Write<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json).ReplaceLineEndings("\n") + "\n");

    private sealed record MissionsJson(IReadOnlyList<MissionDefinition> Missions);

    private sealed record ResourceNodesJson(IReadOnlyList<ResourceNodeKind> Kinds);

    private sealed record TerrainJson(TileRange VariantTiles, int VariantCount,
        TileRange ResourceTiles, TileRange ObstacleTiles, IReadOnlyList<int> ShoreTiles,
        IReadOnlyList<TileKind> Tiles)
    {
        public static TerrainJson From(TerrainCatalog t) => new(t.VariantTiles, t.VariantCount,
            t.ResourceTiles, t.ObstacleTiles, t.ShoreTiles.Order().ToArray(),
            t.Tiles.OrderBy(k => k.Id).ToArray());

        public TerrainCatalog ToCatalog() =>
            new(Tiles, VariantTiles, VariantCount, ResourceTiles, ObstacleTiles, ShoreTiles);
    }

    /// <summary>A map file: one string per row, two hexadecimal digits per cell.</summary>
    private sealed record MapJson(int Id, int Width, int Height, IReadOnlyList<string> Rows)
    {
        public static MapJson From(MapDefinition map)
        {
            var rows = new string[map.Size.Height];
            for (int y = 0; y < rows.Length; y++)
            {
                var row = new StringBuilder(map.Size.Width * 2);
                for (int x = 0; x < map.Size.Width; x++)
                {
                    row.Append(map.TileAt(new GridPoint(x, y)).ToString("x2", CultureInfo.InvariantCulture));
                }
                rows[y] = row.ToString();
            }
            return new MapJson(map.Id, map.Size.Width, map.Size.Height, rows);
        }

        public MapDefinition ToMap()
        {
            if (Rows.Count != Height)
            {
                throw new InvalidDataException($"map {Id}: {Rows.Count} rows, expected {Height}");
            }
            var tiles = new byte[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                string row = Rows[y];
                if (row.Length != Width * 2)
                {
                    throw new InvalidDataException($"map {Id}, row {y}: {row.Length / 2} cells, expected {Width}");
                }
                for (int x = 0; x < Width; x++)
                {
                    tiles[(y * Width) + x] = byte.Parse(row.AsSpan(x * 2, 2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture);
                }
            }
            return new MapDefinition(Id, new GridSize(Width, Height), tiles);
        }
    }
}
