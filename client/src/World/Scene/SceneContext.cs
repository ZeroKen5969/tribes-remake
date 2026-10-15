using System;
using System.Collections.Generic;
using Tribes.Core.World;
using Tribes.Graphics.Composition;
using Tribes.Graphics.Model;

namespace Tribes.Client.World.Scene;

/// <summary>
/// The pictures and tables the scene is composed from, shared by every frame: the sprite model and
/// its composers, the small digit font (floating numbers) and the health bar colours.
/// </summary>
public sealed class SceneAssets
{
    /// <summary>Health bar fill colours by (hit points - 1) * 3 / maximum; the last entry is the original's spare.</summary>
    internal static readonly RgbColor[] HealthColours =
    {
        RgbColor.Parse("#e00000"), RgbColor.Parse("#e4e100"), RgbColor.Parse("#00da00"), RgbColor.Parse("#00da00"),
    };

    /// <summary>Creates the assets from the sprite model and the font model.</summary>
    public SceneAssets(SpriteCatalog catalog, Composers composers, BitmapFontCatalog fonts)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Composers = composers ?? throw new ArgumentNullException(nameof(composers));
        Fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
        SmallFont = fonts.Fonts[1];
        foreach (Glyph glyph in SmallFont.Glyphs)
        {
            smallGlyphs[glyph.Code] = glyph;
        }
    }

    private readonly Dictionary<int, Glyph> smallGlyphs = new();

    /// <summary>The small font's glyph with code <paramref name="code"/> (row * 30 + column); fails like the original's table for other codes.</summary>
    public Glyph SmallGlyph(int code) =>
        smallGlyphs.TryGetValue(code, out Glyph? glyph) ? glyph : throw new IndexOutOfRangeException($"no small glyph {code}");

    /// <summary>The sprite model.</summary>
    public SpriteCatalog Catalog { get; }

    /// <summary>Its composers.</summary>
    public Composers Composers { get; }

    /// <summary>The original's text model.</summary>
    public BitmapFontCatalog Fonts { get; }

    /// <summary>The small digit font of the floating numbers.</summary>
    public BitmapFont SmallFont { get; }

    /// <summary>Width of a map cell in pixels.</summary>
    public int CellWidth => Catalog.Cell.Width;

    /// <summary>Height of a map cell in pixels.</summary>
    public int CellHeight => Catalog.Cell.Height;

    private AllianceHallArt? allianceHall;

    /// <summary>The alliance hall's pictures (a plain stand-in until the drawn sheet is given).</summary>
    public AllianceHallArt AllianceHall
    {
        get => allianceHall ??= AllianceHallArt.Placeholder();
        set => allianceHall = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// How each tribe looks: the original's two outfits (the player's and the computer's clothes, hair
/// and flags) and the skin colour chosen for it. The viewer is the tribe whose fog of war, health
/// bars and hidden units the view follows. No two tribes share a colour while one of the eight is
/// free; a world of more than eight tribes (endless worlds) uses every colour, some more than once.
/// </summary>
public sealed class TribeLooks
{
    private readonly Dictionary<int, int> colours = new();

    /// <summary>Creates the looks for a world of <paramref name="tribes"/> tribes seen by <paramref name="viewer"/>.</summary>
    public TribeLooks(int viewer, int tribes = 2)
    {
        Viewer = viewer;
        Tribes = Math.Max(1, tribes);
    }

    /// <summary>The tribe the view belongs to.</summary>
    public int Viewer { get; }

    /// <summary>Number of tribes in the world.</summary>
    public int Tribes { get; }

    /// <summary>
    /// The outfit of <paramref name="tribe"/>: even tribes wear the original player's clothes, odd
    /// tribes the computer's (so the original two-tribe game looks as it did).
    /// </summary>
    public static Tribe OutfitOf(int tribe) => (tribe & 1) == 0 ? Tribe.Player : Tribe.Computer;

    /// <summary>The colour index of <paramref name="tribe"/> (see <see cref="TribeColours"/>).</summary>
    public int ColourOf(int tribe) => colours.TryGetValue(tribe, out int c) ? c : TribeColours.DefaultFor(tribe);

    /// <summary>
    /// Gives <paramref name="tribe"/> colour <paramref name="colourIndex"/>; false (and nothing
    /// changes) when the index is not a colour or another tribe already has it.
    /// </summary>
    public bool TrySetColour(int tribe, int colourIndex)
    {
        if (colourIndex < 0 || colourIndex >= TribeColours.Count || tribe < 0 || tribe >= Tribes)
        {
            return false;
        }
        var wanted = new int[Tribes];
        for (int t = 0; t < Tribes; t++)
        {
            wanted[t] = t == tribe ? colourIndex : ColourOf(t);
        }
        if (!IsValid(wanted))
        {
            return false;
        }
        colours[tribe] = colourIndex;
        return true;
    }

    /// <summary>
    /// Gives every tribe its colour at once (<paramref name="colourByTribe"/>[t] for tribe t), so
    /// colours can be swapped; false (and nothing changes) when a colour is not 0..7 or repeats while
    /// another is free.
    /// </summary>
    public bool TrySetColours(IReadOnlyList<int> colourByTribe)
    {
        ArgumentNullException.ThrowIfNull(colourByTribe);
        var wanted = new int[Tribes];
        for (int t = 0; t < Tribes; t++)
        {
            wanted[t] = t < colourByTribe.Count ? colourByTribe[t] : ColourOf(t);
        }
        if (!IsValid(wanted))
        {
            return false;
        }
        for (int t = 0; t < Math.Min(Tribes, colourByTribe.Count); t++)
        {
            colours[t] = colourByTribe[t];
        }
        return true;
    }

    /// <summary>
    /// Whether every tribe can have the colour <paramref name="colourByTribe"/> gives it: all are
    /// colours, and none repeats while another colour is free (with more than eight tribes every
    /// colour is used).
    /// </summary>
    public static bool IsValid(IReadOnlyList<int> colourByTribe)
    {
        ArgumentNullException.ThrowIfNull(colourByTribe);
        var seen = new HashSet<int>();
        foreach (int colour in colourByTribe)
        {
            if (colour < 0 || colour >= TribeColours.Count)
            {
                return false;
            }
            seen.Add(colour);
        }
        return seen.Count == Math.Min(colourByTribe.Count, TribeColours.Count);
    }

    /// <summary>
    /// The looks of the same world after more tribes joined it (<paramref name="tribes"/> in all):
    /// the colours stay, each new tribe gets the first colour still free, or once all are used
    /// its default one.
    /// </summary>
    public TribeLooks Grow(int tribes)
    {
        var grown = new TribeLooks(Viewer, Math.Max(tribes, Tribes));
        var used = new HashSet<int>();
        for (int t = 0; t < Tribes; t++)
        {
            grown.colours[t] = ColourOf(t);
            used.Add(ColourOf(t));
        }
        for (int t = Tribes; t < grown.Tribes; t++)
        {
            int colour = TribeColours.DefaultFor(t);
            for (int c = 0; c < TribeColours.Count && used.Contains(colour) && used.Count < TribeColours.Count; c++)
            {
                if (!used.Contains(c))
                {
                    colour = c;
                }
            }
            grown.colours[t] = colour;
            used.Add(colour);
        }
        return grown;
    }

    /// <summary>Whether another tribe than <paramref name="tribe"/> has colour <paramref name="colourIndex"/>.</summary>
    public bool IsTaken(int colourIndex, int tribe)
    {
        for (int other = 0; other < Tribes; other++)
        {
            if (other != tribe && ColourOf(other) == colourIndex)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether <paramref name="tribe"/> is the viewer's own.</summary>
    public bool IsOwn(int tribe) => tribe == Viewer;

    /// <summary>The same looks (every tribe's colour) seen by <paramref name="viewer"/>.</summary>
    public TribeLooks WithViewer(int viewer)
    {
        var looks = new TribeLooks(viewer, Tribes);
        foreach ((int tribe, int colour) in colours)
        {
            looks.colours[tribe] = colour;
        }
        return looks;
    }
}

/// <summary>Everything one composition of the scene reads.</summary>
/// <param name="World">The world (only read).</param>
/// <param name="View">The cells shown.</param>
/// <param name="Presentation">What only the screen keeps (changed by the composition as the original's drawing does).</param>
/// <param name="Marks">Selection marks.</param>
/// <param name="Looks">Tribe outfits, colours and the viewer.</param>
/// <param name="AdvanceEffects">Whether this composition shows a new rule frame (effects move on by one frame).</param>
/// <param name="Sight">
/// What the viewer sees (fog, whose traps and markers); null: the player of <see cref="TribeLooks.Viewer"/>.
/// </param>
public sealed record SceneRequest(GameWorld World, ViewRect View, ScenePresentation Presentation, WorldMarks Marks,
    TribeLooks Looks, bool AdvanceEffects, ViewerSight? Sight = null);
