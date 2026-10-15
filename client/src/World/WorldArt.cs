using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.World.Scene;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;
using Tribes.Graphics.Serialization;

namespace Tribes.Client.World;

/// <summary>
/// The pictures the world view draws with: the original's sheets packed into one atlas texture
/// (and as pixels for the terrain), the alliance hall's sheet of endless worlds, the scene assets,
/// the tribe colours and the sheets recoloured for each tribe colour (added to the atlas on first use).
/// </summary>
public sealed class WorldArt
{
    private const string FontsPath = "res://content/client/fonts.json";
    private const string ColoursPath = "res://content/client/tribe-colours.json";

    /// <summary>The alliance hall's drawn sheet (see <see cref="AllianceHallArt"/>); a plain stand-in is drawn while it is missing.</summary>
    public const string AllianceHallPath = "res://assets/" + AllianceHallArt.AssetFile;

    private static WorldArt? shared;

    private readonly OriginalArt art;
    private readonly Dictionary<string, RgbaImage> pixels = new(StringComparer.Ordinal);
    private readonly SkinRecolour skin;
    private readonly SheetAtlas atlas = new();
    private readonly Dictionary<string, Rect2I> plainRegions = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Sheet, int Colour), Rect2I> colouredRegions = new();
    private readonly HashSet<string> skinSheets;

    private WorldArt()
    {
        art = OriginalArt.Shared;
        var fonts = CatalogJson.Deserialize<BitmapFontCatalog>(ReadText(FontsPath));
        Assets = new SceneAssets(art.Catalog, art.Composers, fonts) { AllianceHall = LoadAllianceHall() };
        pixels[AllianceHall.SheetId] = Assets.AllianceHall.Sheet;
        Colours = TribeColours.Parse(ReadText(ColoursPath));
        skin = new SkinRecolour(Colours, art.Catalog, Pixels);
        skinSheets = new HashSet<string>(skin.SkinSheets, StringComparer.Ordinal);
        foreach (SheetInfo sheet in art.Catalog.Sheets)
        {
            Region(sheet.Id);
        }
        Region(AllianceHall.SheetId);
    }

    /// <summary>The texture holding every sheet (see <see cref="Region(string)"/>).</summary>
    public Texture2D Atlas => atlas.Texture;

    /// <summary>A white pixel of <see cref="Atlas"/>, for fills tinted to their colour.</summary>
    public Rect2 WhitePixel => atlas.White;

    /// <summary>Where sheet <paramref name="sheet"/> is in <see cref="Atlas"/>, as it is.</summary>
    public Rect2I Region(string sheet)
    {
        if (!plainRegions.TryGetValue(sheet, out Rect2I region))
        {
            region = atlas.Add(sheet, Pixels(sheet));
            plainRegions[sheet] = region;
        }
        return region;
    }

    /// <summary>
    /// Where sheet <paramref name="sheet"/> is in <see cref="Atlas"/> as drawn for a villager of
    /// colour <paramref name="colour"/> wearing <paramref name="outfit"/>: the sheet as it is when
    /// it shows no skin or the colour is the outfit's own.
    /// </summary>
    public Rect2I Region(string sheet, int colour, Tribe outfit)
    {
        if (colour < 0 || Colours.IsNative(colour, outfit) || !skinSheets.Contains(sheet))
        {
            return Region(sheet);
        }
        if (!colouredRegions.TryGetValue((sheet, colour), out Rect2I region))
        {
            // The recolouring depends on the colour only; the outfit just decides whether it applies.
            RgbaImage? image = skin.Recolour(sheet, colour, outfit);
            region = image == null ? Region(sheet) : atlas.Add(sheet + "/" + colour.ToString(CultureInfo.InvariantCulture), image);
            colouredRegions[(sheet, colour)] = region;
        }
        return region;
    }

    /// <summary>Adds every sheet recoloured for <paramref name="colour"/> worn with <paramref name="outfit"/> to the atlas now.</summary>
    public void Prepare(int colour, Tribe outfit)
    {
        foreach (string sheet in skinSheets)
        {
            Region(sheet, colour, outfit);
        }
    }

    /// <summary>The art of the running game, loaded on first use.</summary>
    public static WorldArt Shared => shared ??= new WorldArt();

    /// <summary>The sprite model, composers and fonts.</summary>
    public SceneAssets Assets { get; }

    /// <summary>The eight tribe colours.</summary>
    public TribeColours Colours { get; }

    /// <summary>The pixels of sheet <paramref name="sheet"/>.</summary>
    public RgbaImage Pixels(string sheet)
    {
        if (!pixels.TryGetValue(sheet, out RgbaImage? image))
        {
            image = ToRgba(art.SheetImage(sheet));
            pixels[sheet] = image;
        }
        return image;
    }

    /// <summary>A Godot image of <paramref name="image"/>.</summary>
    public static Image ToImage(RgbaImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var bytes = new byte[image.Width * image.Height * 4];
        ReadOnlySpan<int> source = image.Pixels;
        for (int i = 0; i < source.Length; i++)
        {
            int argb = source[i];
            bytes[(i * 4) + 0] = (byte)(argb >> 16);
            bytes[(i * 4) + 1] = (byte)(argb >> 8);
            bytes[(i * 4) + 2] = (byte)argb;
            bytes[(i * 4) + 3] = (byte)(argb >>> 24);
        }
        return Image.CreateFromData(image.Width, image.Height, false, Image.Format.Rgba8, bytes);
    }

    /// <summary>Copies <paramref name="image"/> (RGBA8) into <paramref name="target"/>'s pixel bytes.</summary>
    public static void CopyTo(RgbaImage image, byte[] target)
    {
        ArgumentNullException.ThrowIfNull(image);
        CopyTo(image.Pixels, target);
    }

    /// <summary>Copies ARGB pixels <paramref name="source"/> into <paramref name="target"/> as RGBA8 bytes.</summary>
    public static void CopyTo(ReadOnlySpan<int> source, byte[] target)
    {
        ArgumentNullException.ThrowIfNull(target);
        for (int i = 0; i < source.Length; i++)
        {
            int argb = source[i];
            target[(i * 4) + 0] = (byte)(argb >> 16);
            target[(i * 4) + 1] = (byte)(argb >> 8);
            target[(i * 4) + 2] = (byte)argb;
            target[(i * 4) + 3] = (byte)(argb >>> 24);
        }
    }

    private static AllianceHallArt LoadAllianceHall()
    {
        if (!ResourceLoader.Exists(AllianceHallPath))
        {
            return AllianceHallArt.Placeholder();
        }
        try
        {
            Image image = GD.Load<Texture2D>(AllianceHallPath)?.GetImage()
                ?? throw new InvalidOperationException("the picture cannot be read");
            if (image.IsCompressed())
            {
                image.Decompress();
            }
            if (image.GetFormat() != Image.Format.Rgba8)
            {
                image.Convert(Image.Format.Rgba8);
            }
            return AllianceHallArt.FromSheet(ToRgba(image));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            GD.PushWarning($"{AllianceHallPath}: {e.Message}; the plain stand-in is drawn instead");
            return AllianceHallArt.Placeholder();
        }
    }

    private static RgbaImage ToRgba(Image image)
    {
        byte[] data = image.GetData();
        var result = new RgbaImage(image.GetWidth(), image.GetHeight());
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                int i = ((y * result.Width) + x) * 4;
                result[x, y] = (data[i + 3] << 24) | (data[i] << 16) | (data[i + 1] << 8) | data[i + 2];
            }
        }
        return result;
    }

    private static string ReadText(string path)
    {
        string text = Godot.FileAccess.GetFileAsString(path);
        if (text.Length == 0)
        {
            throw new InvalidOperationException($"cannot read {path}: {Godot.FileAccess.GetOpenError()}");
        }
        return text;
    }
}
