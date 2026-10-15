using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Imaging;
using Tribes.Graphics.Model;

namespace Tribes.Client.World.Scene;

/// <summary>One of the fixed tribe colours: its name, its swatch and the skin tones it gives the tribe.</summary>
/// <param name="Id">Colour index 0..7.</param>
/// <param name="Name">Name in Traditional Chinese.</param>
/// <param name="NameEn">Name in English.</param>
/// <param name="Swatch">The colour shown in menus (the skin's lightest tone).</param>
/// <param name="Native">
/// The outfit whose original pictures already have this skin ("player" or "computer"); those are
/// drawn unchanged, so the original two tribes look exactly as in the original.
/// </param>
/// <param name="Skin">The skin tones by level, from the highlight (0) to the outline (9).</param>
public sealed record TribeColour(int Id, string Name, string NameEn, string Swatch, string? Native, IReadOnlyList<string> Skin);

/// <summary>Where the skin is on a family of sheets: colour of the base sheet's pixel to skin level.</summary>
/// <param name="Sheets">The sheets of the family (the base sheet first, then its palette variants).</param>
/// <param name="Levels">Skin level of each colour of the base sheet.</param>
/// <param name="ComputerLevels">For the torso sheet: the levels inside the computer's torso pictures.</param>
/// <param name="ChangedIn">
/// A palette variant that recolours exactly the skin (the computer's limbs): only pixels that
/// differ between it and the base sheet count as skin.
/// </param>
public sealed record SkinFamily(IReadOnlyList<string> Sheets, IReadOnlyDictionary<string, int> Levels,
    IReadOnlyDictionary<string, int>? ComputerLevels, string? ChangedIn);

/// <summary>
/// The tribe colours (data/client/tribe-colours.json): eight fixed colours a player picks for
/// their tribe; the colour gives the tribe's people their skin. No two tribes of a game share one.
/// </summary>
/// <param name="Format">Format name and version.</param>
/// <param name="Colours">The colours by index.</param>
/// <param name="Families">Where the skin is on the villager sheets.</param>
/// <param name="Faces">The skin of the face pictures (portraits), null when the data has none.</param>
public sealed record TribeColours(string Format, IReadOnlyList<TribeColour> Colours, IReadOnlyList<SkinFamily> Families, FaceSkin? Faces = null)
{
    /// <summary>Number of colours.</summary>
    public const int Count = 8;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The colour a tribe has until one is chosen: tribe t gets colour t (the original's two tribes keep their look).</summary>
    public static int DefaultFor(int tribe) => ((tribe % Count) + Count) % Count;

    /// <summary>Reads the colours from JSON text.</summary>
    public static TribeColours Parse(string json)
    {
        TribeColours colours = JsonSerializer.Deserialize<TribeColours>(json, Json) ?? throw new InvalidDataException("empty tribe colours");
        if (colours.Colours.Count != Count)
        {
            throw new InvalidDataException($"expected {Count} tribe colours, found {colours.Colours.Count}");
        }
        for (int i = 0; i < Count; i++)
        {
            if (colours.Colours[i].Id != i || colours.Colours[i].Skin.Count == 0)
            {
                throw new InvalidDataException($"tribe colour {i} is malformed");
            }
        }
        return colours;
    }

    /// <summary>Whether colour <paramref name="colour"/> is the original look of <paramref name="outfit"/>.</summary>
    public bool IsNative(int colour, Tribe outfit)
    {
        string? native = Colours[colour].Native;
        return native != null && string.Equals(native, outfit == Tribe.Player ? "player" : "computer", StringComparison.Ordinal);
    }

    /// <summary>The colour whose skin a face picture shows as drawn: the computer's for its portraits, the player's for every other.</summary>
    public int FaceNative(int portrait) => NativeOf(Faces != null && Faces.ComputerPortraits.Contains(portrait) ? Tribe.Computer : Tribe.Player);

    /// <summary>
    /// The face pictures' skin colours (0xRRGGBB) and what each becomes in colour <paramref name="colour"/>
    /// (see <see cref="FaceSkin"/>); empty when the data lists no face skin. Made anew on each call
    /// (a few dozen colours); callers keep the pictures they make with it.
    /// </summary>
    public IReadOnlyDictionary<int, int> FaceSkinMap(int colour)
    {
        if (colour < 0 || colour >= Colours.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(colour), colour, $"colours are 0..{Colours.Count - 1}");
        }
        return Faces?.Map(Colours[NativeOf(Tribe.Player)].Skin, Colours[colour].Skin) ?? new Dictionary<int, int>();
    }

    /// <summary>The colour that is the original look of <paramref name="outfit"/> (0 when the data names none).</summary>
    private int NativeOf(Tribe outfit)
    {
        for (int c = 0; c < Colours.Count; c++)
        {
            if (IsNative(c, outfit))
            {
                return c;
            }
        }
        return 0;
    }
}

/// <summary>
/// Recolours the villager sheets for a tribe colour: every skin pixel (found by its colour on the
/// base sheet, so the palette variants of a sheet change in the same places) takes the colour's
/// tone of the same level. Engine independent; the world view turns the results into textures.
/// </summary>
public sealed class SkinRecolour
{
    private readonly TribeColours colours;
    private readonly Func<string, RgbaImage> sheets;
    private readonly Dictionary<string, (SkinFamily Family, string Base)> familyOf = new(StringComparer.Ordinal);
    private readonly Dictionary<string, sbyte[]> levelMaps = new(StringComparer.Ordinal);
    private readonly bool[] computerTorso;
    private readonly int torsoWidth;
    private readonly int[][] ramps;

    /// <summary>Creates the recolouring of the sheets of <paramref name="catalog"/>.</summary>
    public SkinRecolour(TribeColours colours, SpriteCatalog catalog, Func<string, RgbaImage> sheets)
    {
        this.colours = colours ?? throw new ArgumentNullException(nameof(colours));
        ArgumentNullException.ThrowIfNull(catalog);
        this.sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        foreach (SkinFamily family in colours.Families)
        {
            foreach (string sheet in family.Sheets)
            {
                familyOf[sheet] = (family, family.Sheets[0]);
            }
        }
        ramps = new int[colours.Colours.Count][];
        for (int c = 0; c < ramps.Length; c++)
        {
            ramps[c] = Array.ConvertAll(new List<string>(colours.Colours[c].Skin).ToArray(), s => RgbColor.Parse(s).Value);
        }
        RgbaImage torso = sheets(TorsoSheet);
        torsoWidth = torso.Width;
        computerTorso = ComputerTorsoArea(catalog, torso.Width, torso.Height);
    }

    private const string TorsoSheet = "pi0_05";
    private const int TorsoSheetNumber = 5;

    /// <summary>The sheets that show skin.</summary>
    public IEnumerable<string> SkinSheets => familyOf.Keys;

    /// <summary>
    /// Sheet <paramref name="sheet"/> recoloured for colour <paramref name="colour"/> worn with
    /// <paramref name="outfit"/>; null when the sheet shows no skin or the colour is the outfit's own.
    /// </summary>
    public RgbaImage? Recolour(string sheet, int colour, Tribe outfit)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (colour < 0 || colour >= ramps.Length || colours.IsNative(colour, outfit) || !familyOf.TryGetValue(sheet, out var family))
        {
            return null;
        }
        sbyte[] levels = LevelMap(family.Family, family.Base);
        RgbaImage source = sheets(sheet);
        var result = new RgbaImage(source.Width, source.Height);
        int[] ramp = ramps[colour];
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int argb = source[x, y];
                int level = levels.Length == source.Width * source.Height ? levels[(y * source.Width) + x] : -1;
                result[x, y] = level >= 0 && level < ramp.Length && (argb >>> 24) != 0 ? unchecked((int)0xFF000000) | ramp[level] : argb;
            }
        }
        return result;
    }

    private sbyte[] LevelMap(SkinFamily family, string baseSheet)
    {
        if (levelMaps.TryGetValue(baseSheet, out sbyte[]? map))
        {
            return map;
        }
        RgbaImage image = sheets(baseSheet);
        RgbaImage? changed = family.ChangedIn != null ? sheets(family.ChangedIn) : null;
        var levels = ToArgb(family.Levels);
        var computer = family.ComputerLevels is { } c ? ToArgb(c) : null;
        map = new sbyte[image.Width * image.Height];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int rgb = image[x, y] & 0xFFFFFF;
                if (changed != null && (changed[x, y] & 0xFFFFFF) == rgb)
                {
                    map[(y * image.Width) + x] = -1;
                    continue;
                }
                Dictionary<int, int> table = computer != null && baseSheet == TorsoSheet && computerTorso[(y * torsoWidth) + x] ? computer : levels;
                map[(y * image.Width) + x] = (image[x, y] >>> 24) != 0 && table.TryGetValue(rgb, out int level) ? (sbyte)level : (sbyte)-1;
            }
        }
        levelMaps[baseSheet] = map;
        return map;
    }

    private static Dictionary<int, int> ToArgb(IReadOnlyDictionary<string, int> table)
    {
        var result = new Dictionary<int, int>();
        foreach (var (colour, level) in table)
        {
            result[RgbColor.Parse(colour).Value] = level;
        }
        return result;
    }

    /// <summary>
    /// The pixels of the torso sheet that belong to the computer's adult torsos: pose torso
    /// sprites + 36 (male) and + 54 (female), in both styles (+ 72).
    /// </summary>
    private static bool[] ComputerTorsoArea(SpriteCatalog catalog, int width, int height)
    {
        var area = new bool[width * height];
        var bases = new HashSet<int>();
        foreach (Pose pose in catalog.Poses)
        {
            foreach (PoseSlot slot in pose.Slots)
            {
                if (slot.Type == SlotType.Torso && slot.Source == PartSource.Sprite && slot.Id > 1)
                {
                    bases.Add(slot.Id);
                }
            }
        }
        foreach (int first in bases)
        {
            foreach (int offset in new[] { 36, 54, 108, 126 })
            {
                int sprite = first + offset;
                if (sprite >= catalog.Sprites.Count)
                {
                    continue;
                }
                foreach (SpritePart part in catalog.Sprites[sprite].Parts)
                {
                    if (part.Piece <= 0 || part.Piece >= catalog.Pieces.Count)
                    {
                        continue;
                    }
                    Piece piece = catalog.Pieces[part.Piece];
                    if (piece.Shape != PieceShape.Image || piece.Sheet != TorsoSheetNumber)
                    {
                        continue;
                    }
                    for (int y = Math.Max(0, piece.Y); y < Math.Min(height, piece.Y + piece.Height); y++)
                    {
                        for (int x = Math.Max(0, piece.X); x < Math.Min(width, piece.X + piece.Width); x++)
                        {
                            area[(y * width) + x] = true;
                        }
                    }
                }
            }
        }
        return area;
    }
}
