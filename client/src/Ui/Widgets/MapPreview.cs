using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Art;
using Tribes.Core.Content;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// A mission's map drawn like the original minimap: one pixel per cell in the minimap colour of
/// its terrain class (interface.json "minimapColors", colours from ui.json), the start marked
/// with a yellow cross, shown at the largest whole-number scale that fits.
/// </summary>
public partial class MapPreview : Control
{
    private const string UiFile = "res://content/client/ui.json";
    private static Color[]? terrainColours;

    private Texture2D? map;
    private Vector2I start;

    /// <summary>Shows the map of mission <paramref name="mission"/> (nothing when it has none).</summary>
    public void ShowMission(int mission)
    {
        map = null;
        GameContent content = GameApp.Instance.Content;
        if (content.Missions.TryGetValue(mission, out MissionDefinition? definition)
            && content.Maps.TryGetValue(definition.MapId, out MapDefinition? source))
        {
            map = Paint(content, source, definition, out start);
        }
        QueueRedraw();
    }

    private static Texture2D Paint(GameContent content, MapDefinition source, MissionDefinition mission, out Vector2I start)
    {
        Color[] colours = TerrainColours();
        IReadOnlyList<int> classes = content.Rules.Interface.MinimapColors;
        int width = Math.Min(mission.PlayableSize.Width, source.Size.Width);
        int height = Math.Min(mission.PlayableSize.Height, source.Size.Height);
        Image image = PixelPainter.Blank(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int tile = source.TileAt(new GridPoint(x, y));
                int terrain = tile >= 0 && tile < classes.Count ? classes[tile] : 0;
                image.SetPixel(x, y, colours[Math.Clamp(terrain, 0, colours.Length - 1)]);
            }
        }
        start = new Vector2I(mission.StartCenter.X, mission.StartCenter.Y);
        return ImageTexture.CreateFromImage(image);
    }

    private static Color[] TerrainColours()
    {
        if (terrainColours is not null)
        {
            return terrainColours;
        }
        var colours = new Color[6];
        for (int i = 0; i < colours.Length; i++)
        {
            colours[i] = UiPalette.Ground;
        }
        string json = Godot.FileAccess.GetFileAsString(UiFile);
        if (json.Length > 0)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            foreach (JsonElement entry in document.RootElement.GetProperty("colors").EnumerateArray())
            {
                string name = entry.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("minimapTerrain", StringComparison.Ordinal)
                    && int.TryParse(name["minimapTerrain".Length..], out int index) && index >= 0 && index < colours.Length)
                {
                    colours[index] = new Color(entry.GetProperty("color").GetString() ?? "#000000");
                }
            }
        }
        terrainColours = colours;
        return colours;
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (map is null)
        {
            return;
        }
        Vector2 size = map.GetSize();
        int scale = Math.Max(1, (int)Math.Min((Size.X - 4) / size.X, (Size.Y - 4) / size.Y));
        Vector2 drawn = size * scale;
        Vector2 at = new(MathF.Floor((Size.X - drawn.X) / 2), MathF.Floor((Size.Y - drawn.Y) / 2));
        DrawRect(new Rect2(at - new Vector2(2, 2), drawn + new Vector2(4, 4)), UiPalette.Ink);
        DrawRect(new Rect2(at - Vector2.One, drawn + new Vector2(2, 2)), UiPalette.Brown);
        DrawTextureRect(map, new Rect2(at, drawn), false);
        Vector2 mark = at + (new Vector2(start.X, start.Y) + new Vector2(0.5f, 0.5f)) * scale;
        mark = mark.Floor();
        DrawRect(new Rect2(mark.X - 3, mark.Y, 7, 1), UiPalette.Yellow);
        DrawRect(new Rect2(mark.X, mark.Y - 3, 1, 7), UiPalette.Yellow);
    }
}
