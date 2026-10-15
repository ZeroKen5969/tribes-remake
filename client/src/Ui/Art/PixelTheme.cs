using Godot;
using Tribes.Graphics.Composition;
using static Tribes.Client.Ui.Art.PixelPainter;

namespace Tribes.Client.Ui.Art;

/// <summary>
/// The interface theme, built when the game starts from the original panel pieces (parchment
/// sheet, speech bubble, icon bar slots, tick and cross, menu arrows) and from new pieces painted
/// in the same palette and box style. Every size is in logical pixels; the window shows them at a
/// whole-number scale (see GameApp). Type variations used by the screens are named in
/// <see cref="Variations"/>.
/// </summary>
public static class PixelTheme
{
    /// <summary>Body text size: Cubic 11 is drawn on a 12-pixel grid.</summary>
    public const int BodySize = 12;

    /// <summary>Title and menu size: the 12-pixel font doubled.</summary>
    public const int LargeSize = 24;

    /// <summary>Height of a touch-friendly row.</summary>
    public const int RowHeight = 24;

    private const string CubicPath = "res://assets/fonts/cubic-11/Cubic_11.ttf";
    private const string OriginalFontPath = "res://assets/fonts/original-large.fnt";

    // Sprites and pieces of the original (sprites.json): see data/client/ui.json.
    private const int SheetPanelSprite = 1656;
    private const int SelectedSlotSprite = 548;
    private const int SlotSprite = 549;
    private const int TickPiece = 1506;
    private const int CrossPiece = 1509;

    private static Theme? current;

    /// <summary>Names of the theme type variations.</summary>
    public static class Variations
    {
        /// <summary>PanelContainer: the torn parchment sheet of the original menus.</summary>
        public const string Sheet = "SheetPanel";

        /// <summary>PanelContainer: a sunken light parchment box (lists, text).</summary>
        public const string Inset = "InsetPanel";

        /// <summary>PanelContainer: an icon bar slot (list rows, slots).</summary>
        public const string Slot = "SlotPanel";

        /// <summary>PanelContainer: the selected icon bar slot.</summary>
        public const string SlotSelected = "SlotSelectedPanel";

        /// <summary>PanelContainer: the speech bubble (messages, notes).</summary>
        public const string Bubble = "BubblePanel";

        /// <summary>PanelContainer: a small gold badge marking a state (「對局中」).</summary>
        public const string Badge = "BadgePanel";

        /// <summary>PanelContainer: a dark veil behind dialogs.</summary>
        public const string Veil = "VeilPanel";

        /// <summary>Button: a menu entry drawn like the original menus (large yellow text, arrows when chosen).</summary>
        public const string MenuItem = "MenuItemButton";

        /// <summary>Button: the main action of a screen.</summary>
        public const string Primary = "PrimaryButton";

        /// <summary>Button: a small square button around an icon.</summary>
        public const string Tool = "ToolButton";

        /// <summary>Label: a screen title in the large yellow outlined text.</summary>
        public const string Title = "TitleLabel";

        /// <summary>Label: a section heading.</summary>
        public const string Heading = "HeadingLabel";

        /// <summary>Label: secondary text.</summary>
        public const string Muted = "MutedLabel";

        /// <summary>Label: light text on the sky or dark backgrounds.</summary>
        public const string OnDark = "OnDarkLabel";

        /// <summary>Label: the original bitmap font (Latin letters and digits only).</summary>
        public const string Original = "OriginalLabel";
    }

    /// <summary>The theme, built on first use.</summary>
    public static Theme Current => current ??= Build();

    /// <summary>Cubic 11, set up for crisp pixels.</summary>
    public static FontFile BodyFont { get; private set; } = null!;

    /// <summary>The original large bitmap font.</summary>
    public static FontFile? OriginalFont { get; private set; }

    /// <summary>The green tick of the original confirm key.</summary>
    public static Texture2D TickIcon { get; private set; } = null!;

    /// <summary>The red cross of the original cancel key.</summary>
    public static Texture2D CrossIcon { get; private set; } = null!;

    private static Theme Build()
    {
        OriginalArt art = OriginalArt.Shared;
        BodyFont = GD.Load<FontFile>(CubicPath);
        BodyFont.Antialiasing = TextServer.FontAntialiasing.None;
        BodyFont.Hinting = TextServer.Hinting.None;
        BodyFont.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
        BodyFont.GenerateMipmaps = false;
        OriginalFont = ResourceLoader.Exists(OriginalFontPath) ? GD.Load<FontFile>(OriginalFontPath) : null;
        ThemeDB.FallbackFont = BodyFont;
        ThemeDB.FallbackFontSize = BodySize;

        Image tick = art.Rasterize(art.Composers.Sprites.ComposePiece(TickPiece).Parts, out _);
        Image cross = art.Rasterize(art.Composers.Sprites.ComposePiece(CrossPiece).Parts, out _);
        TickIcon = ImageTexture.CreateFromImage(tick);
        CrossIcon = ImageTexture.CreateFromImage(cross);

        var theme = new Theme { DefaultFont = BodyFont, DefaultFontSize = BodySize };
        AddLabels(theme);
        AddPanels(theme, art);
        AddButtons(theme);
        AddInputs(theme, tick);
        AddLists(theme);
        AddTabs(theme);
        AddScrollBars(theme);
        AddSliders(theme);
        return theme;
    }

    // ---- styles ------------------------------------------------------------------------------

    private static readonly BoxStyle Raised = new(UiPalette.Brown, UiPalette.Parchment, UiPalette.ParchmentLight, UiPalette.ParchmentDark, new Color(UiPalette.Ink, 0.6f));
    private static readonly BoxStyle RaisedHover = new(UiPalette.Ink, UiPalette.ParchmentLight, UiPalette.ParchmentPale, UiPalette.ParchmentMid, new Color(UiPalette.Ink, 0.6f));
    private static readonly BoxStyle Pushed = new(UiPalette.Ink, UiPalette.ParchmentDark, UiPalette.ParchmentShadow, UiPalette.ParchmentMid, UiPalette.Clear);
    private static readonly BoxStyle Dull = new(UiPalette.ParchmentShadow, UiPalette.ParchmentMid, UiPalette.ParchmentMid, UiPalette.ParchmentDark, UiPalette.Clear);
    private static readonly BoxStyle Sunken = new(UiPalette.Brown, UiPalette.ParchmentLight, UiPalette.ParchmentMid, UiPalette.ParchmentPale, UiPalette.Clear);
    private static readonly BoxStyle SunkenFocus = new(UiPalette.Ink, UiPalette.ParchmentPale, UiPalette.ParchmentMid, UiPalette.ParchmentPale, UiPalette.Clear);
    private static readonly BoxStyle SunkenDull = new(UiPalette.ParchmentShadow, UiPalette.ParchmentMid, UiPalette.ParchmentDark, UiPalette.ParchmentMid, UiPalette.Clear);
    private static readonly BoxStyle Groove = new(UiPalette.Brown, UiPalette.ParchmentDark, UiPalette.ParchmentShadow, UiPalette.ParchmentMid, UiPalette.Clear);
    private static readonly BoxStyle Gold = new(UiPalette.Ink, UiPalette.Yellow, UiPalette.YellowLight, UiPalette.Orange, new Color(UiPalette.Ink, 0.6f));
    private static readonly BoxStyle GoldHover = new(UiPalette.Ink, UiPalette.YellowLight, new Color("#fafa90"), UiPalette.Yellow, new Color(UiPalette.Ink, 0.6f));
    private static readonly BoxStyle GoldPushed = new(UiPalette.Ink, UiPalette.Orange, new Color("#c07800"), UiPalette.Yellow, UiPalette.Clear);
    private static readonly BoxStyle Bar = new(UiPalette.Clear, UiPalette.Green, UiPalette.GreenBright, new Color("#3a7d12"), UiPalette.Clear);
    private static readonly BoxStyle Band = new(UiPalette.Clear, UiPalette.ParchmentLight, UiPalette.ParchmentPale, UiPalette.ParchmentMid, UiPalette.Clear);
    private static readonly BoxStyle BandDark = new(UiPalette.Clear, UiPalette.ParchmentDark, UiPalette.ParchmentShadow, UiPalette.ParchmentMid, UiPalette.Clear);

    /// <summary>A box image of <paramref name="width"/> x <paramref name="height"/> plus a shadow row when the style has one.</summary>
    private static Image BoxImage(int width, int height, BoxStyle style, int top = 0)
    {
        Image image = Blank(width + 1, height + top + 1);
        Box(image, new Rect2I(0, top, width, height), style);
        return image;
    }

    /// <summary>A nine-slice style box.</summary>
    private static StyleBoxTexture Slice(Image image, int margin, int padX, int padY, int padBottom = -1, bool tile = false)
    {
        var box = new StyleBoxTexture
        {
            Texture = ImageTexture.CreateFromImage(image),
            TextureMarginLeft = margin,
            TextureMarginTop = margin,
            TextureMarginRight = margin + 1,
            TextureMarginBottom = margin + 1,
            ContentMarginLeft = padX,
            ContentMarginRight = padX,
            ContentMarginTop = padY,
            ContentMarginBottom = padBottom < 0 ? padY : padBottom,
        };
        if (tile)
        {
            box.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.TileFit;
            box.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.TileFit;
        }
        return box;
    }

    private static StyleBoxEmpty Empty(int padX = 0, int padY = 0) => new()
    {
        ContentMarginLeft = padX,
        ContentMarginRight = padX,
        ContentMarginTop = padY,
        ContentMarginBottom = padY,
    };

    // ---- labels ------------------------------------------------------------------------------

    private static void AddLabels(Theme theme)
    {
        theme.SetColor("font_color", "Label", UiPalette.Ink);
        theme.SetConstant("line_spacing", "Label", 2);

        Variation(theme, Variations.Title, "Label");
        theme.SetFontSize("font_size", Variations.Title, LargeSize);
        theme.SetColor("font_color", Variations.Title, UiPalette.Yellow);
        theme.SetColor("font_outline_color", Variations.Title, UiPalette.Ink);
        theme.SetConstant("outline_size", Variations.Title, 4);
        theme.SetColor("font_shadow_color", Variations.Title, new Color(UiPalette.Ink, 0.5f));
        theme.SetConstant("shadow_offset_x", Variations.Title, 2);
        theme.SetConstant("shadow_offset_y", Variations.Title, 2);

        Variation(theme, Variations.Heading, "Label");
        theme.SetColor("font_color", Variations.Heading, UiPalette.Ink);
        theme.SetColor("font_shadow_color", Variations.Heading, UiPalette.ParchmentPale);
        theme.SetConstant("shadow_offset_x", Variations.Heading, 1);
        theme.SetConstant("shadow_offset_y", Variations.Heading, 1);

        Variation(theme, Variations.Muted, "Label");
        theme.SetColor("font_color", Variations.Muted, UiPalette.Brown);

        Variation(theme, Variations.OnDark, "Label");
        theme.SetColor("font_color", Variations.OnDark, UiPalette.Paper);
        theme.SetColor("font_outline_color", Variations.OnDark, UiPalette.Ink);
        theme.SetConstant("outline_size", Variations.OnDark, 2);

        Variation(theme, Variations.Original, "Label");
        if (OriginalFont != null)
        {
            theme.SetFont("font", Variations.Original, OriginalFont);
        }
        theme.SetColor("font_color", Variations.Original, Colors.White);

        theme.SetColor("default_color", "RichTextLabel", UiPalette.Ink);
        theme.SetConstant("line_separation", "RichTextLabel", 2);
        theme.SetStylebox("normal", "RichTextLabel", Empty());
        theme.SetStylebox("focus", "RichTextLabel", Empty());
    }

    private static void Variation(Theme theme, string name, string baseType) => theme.SetTypeVariation(name, baseType);

    // ---- panels ------------------------------------------------------------------------------

    private static void AddPanels(Theme theme, OriginalArt art)
    {
        Image sheet = art.Rasterize(art.Composers.Sprites.ComposeSprite(SheetPanelSprite).Parts, out _);
        var sheetBox = new StyleBoxTexture
        {
            Texture = ImageTexture.CreateFromImage(sheet),
            // The torn edges and corner knots lie within these margins; the middle is plain parchment.
            TextureMarginLeft = 16,
            TextureMarginRight = 18,
            TextureMarginTop = 12,
            TextureMarginBottom = 12,
            ContentMarginLeft = 16,
            ContentMarginRight = 17,
            ContentMarginTop = 12,
            ContentMarginBottom = 13,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.TileFit,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.TileFit,
        };
        theme.SetStylebox("panel", "PanelContainer", Empty());
        theme.SetStylebox("panel", "Panel", Empty());
        PanelVariation(theme, Variations.Sheet, sheetBox);
        PanelVariation(theme, Variations.Inset, Slice(BoxImage(12, 12, Sunken), 3, 5, 4));
        Image slot = art.Rasterize(art.Composers.Sprites.ComposeSprite(SlotSprite).Parts, out _);
        Image selected = art.Rasterize(art.Composers.Sprites.ComposeSprite(SelectedSlotSprite).Parts, out _);
        PanelVariation(theme, Variations.Slot, Slice(slot, 5, 5, 3));
        PanelVariation(theme, Variations.SlotSelected, Slice(selected, 6, 6, 4));
        PanelVariation(theme, Variations.Bubble, Slice(BoxImage(12, 12, new BoxStyle(UiPalette.Brown, UiPalette.ParchmentLight, UiPalette.ParchmentPale, UiPalette.ParchmentMid, new Color(UiPalette.Ink, 0.5f))), 3, 6, 4, 5));
        PanelVariation(theme, Variations.Veil, new StyleBoxFlat { BgColor = new Color(UiPalette.Dark, 0.6f) });
        PanelVariation(theme, Variations.Badge, Slice(BoxImage(10, 10, new BoxStyle(UiPalette.Ink, UiPalette.Orange, UiPalette.Yellow, UiPalette.Gold, UiPalette.Clear)), 3, 4, 0, 1));

        theme.SetStylebox("panel", "TooltipPanel", Slice(BoxImage(12, 12, new BoxStyle(UiPalette.Ink, UiPalette.ParchmentLight, UiPalette.ParchmentPale, UiPalette.ParchmentMid, new Color(UiPalette.Ink, 0.5f))), 3, 5, 3, 4));
        theme.SetColor("font_color", "TooltipLabel", UiPalette.Ink);

        Image rule = Blank(4, 2);
        Fill(rule, new Rect2I(0, 0, 4, 1), UiPalette.ParchmentShadow);
        Fill(rule, new Rect2I(0, 1, 4, 1), UiPalette.ParchmentPale);
        var separator = new StyleBoxTexture { Texture = ImageTexture.CreateFromImage(rule), AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile };
        separator.ContentMarginTop = 1;
        separator.ContentMarginBottom = 1;
        theme.SetStylebox("separator", "HSeparator", separator);
        theme.SetConstant("separation", "HSeparator", 6);
    }

    private static void PanelVariation(Theme theme, string name, StyleBox box)
    {
        Variation(theme, name, "PanelContainer");
        theme.SetStylebox("panel", name, box);
    }

    // ---- buttons -----------------------------------------------------------------------------

    private static void AddButtons(Theme theme)
    {
        const int padX = 8, padTop = 4, padBottom = 5;
        StyleBoxTexture normal = Slice(BoxImage(16, 14, Raised), 4, padX, padTop, padBottom);
        StyleBoxTexture hover = Slice(BoxImage(16, 14, RaisedHover), 4, padX, padTop, padBottom);
        StyleBoxTexture pressed = Slice(BoxImage(16, 14, Pushed, top: 1), 4, padX, padTop + 1, padBottom - 1);
        StyleBoxTexture disabled = Slice(BoxImage(16, 14, Dull), 4, padX, padTop, padBottom);
        StyleBoxFlat focus = new()
        {
            DrawCenter = false,
            BorderColor = UiPalette.Yellow,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            ExpandMarginLeft = 1,
            ExpandMarginTop = 1,
            ExpandMarginRight = 1,
            ExpandMarginBottom = 1,
            AntiAliasing = false,
        };
        foreach (string type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            ButtonStyles(theme, type, normal, hover, pressed, disabled, focus);
        }
        theme.SetConstant("h_separation", "Button", 4);
        theme.SetConstant("icon_max_width", "Button", 16);

        Variation(theme, Variations.Primary, "Button");
        ButtonStyles(theme, Variations.Primary,
            Slice(BoxImage(16, 14, Gold), 4, padX, padTop, padBottom),
            Slice(BoxImage(16, 14, GoldHover), 4, padX, padTop, padBottom),
            Slice(BoxImage(16, 14, GoldPushed, top: 1), 4, padX, padTop + 1, padBottom - 1),
            disabled, focus);

        Variation(theme, Variations.Tool, "Button");
        ButtonStyles(theme, Variations.Tool,
            Slice(BoxImage(16, 14, Raised), 4, 3, 2, 3),
            Slice(BoxImage(16, 14, RaisedHover), 4, 3, 2, 3),
            Slice(BoxImage(16, 14, Pushed, top: 1), 4, 3, 3, 2),
            Slice(BoxImage(16, 14, Dull), 4, 3, 2, 3), focus);

        Variation(theme, Variations.MenuItem, "Button");
        theme.SetStylebox("normal", Variations.MenuItem, Empty(22, 1));
        theme.SetStylebox("hover", Variations.MenuItem, Slice(BoxImage(12, 12, Band), 3, 22, 1));
        theme.SetStylebox("pressed", Variations.MenuItem, Slice(BoxImage(12, 12, BandDark), 3, 22, 1));
        theme.SetStylebox("hover_pressed", Variations.MenuItem, Slice(BoxImage(12, 12, BandDark), 3, 22, 1));
        theme.SetStylebox("disabled", Variations.MenuItem, Empty(22, 1));
        theme.SetStylebox("focus", Variations.MenuItem, Empty());
        theme.SetFontSize("font_size", Variations.MenuItem, LargeSize);
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
        {
            theme.SetColor(state, Variations.MenuItem, UiPalette.Yellow);
        }
        theme.SetColor("font_hover_color", Variations.MenuItem, UiPalette.YellowLight);
        theme.SetColor("font_disabled_color", Variations.MenuItem, UiPalette.ParchmentDark);
        theme.SetColor("font_outline_color", Variations.MenuItem, UiPalette.Ink);
        theme.SetConstant("outline_size", Variations.MenuItem, 4);

        theme.SetIcon("arrow", "OptionButton", ImageTexture.CreateFromImage(DownArrow(7, UiPalette.Yellow, UiPalette.Ink)));
        theme.SetConstant("arrow_margin", "OptionButton", 4);
    }

    private static void ButtonStyles(Theme theme, string type, StyleBox normal, StyleBox hover, StyleBox pressed, StyleBox disabled, StyleBox focus)
    {
        theme.SetStylebox("normal", type, normal);
        theme.SetStylebox("hover", type, hover);
        theme.SetStylebox("pressed", type, pressed);
        theme.SetStylebox("hover_pressed", type, pressed);
        theme.SetStylebox("disabled", type, disabled);
        theme.SetStylebox("focus", type, focus);
        theme.SetColor("font_color", type, UiPalette.Ink);
        theme.SetColor("font_hover_color", type, UiPalette.Ink);
        theme.SetColor("font_pressed_color", type, UiPalette.Ink);
        theme.SetColor("font_hover_pressed_color", type, UiPalette.Ink);
        theme.SetColor("font_focus_color", type, UiPalette.Ink);
        theme.SetColor("font_disabled_color", type, UiPalette.ParchmentShadow);
        theme.SetColor("icon_disabled_color", type, new Color(1, 1, 1, 0.4f));
    }

    // ---- line edits, check boxes, popups ------------------------------------------------------

    private static void AddInputs(Theme theme, Image tick)
    {
        StyleBoxTexture field = Slice(BoxImage(12, 12, Sunken), 3, 5, 4);
        theme.SetStylebox("normal", "LineEdit", field);
        theme.SetStylebox("focus", "LineEdit", Slice(BoxImage(12, 12, SunkenFocus), 3, 5, 4));
        theme.SetStylebox("read_only", "LineEdit", Slice(BoxImage(12, 12, SunkenDull), 3, 5, 4));
        theme.SetColor("font_color", "LineEdit", UiPalette.Ink);
        theme.SetColor("font_placeholder_color", "LineEdit", UiPalette.ParchmentShadow);
        theme.SetColor("caret_color", "LineEdit", UiPalette.Ink);
        theme.SetColor("selection_color", "LineEdit", new Color(UiPalette.Gold, 0.5f));
        theme.SetColor("font_selected_color", "LineEdit", UiPalette.Ink);
        theme.SetColor("font_uneditable_color", "LineEdit", UiPalette.Brown);
        theme.SetConstant("caret_width", "LineEdit", 1);
        theme.SetConstant("minimum_character_width", "LineEdit", 6);

        theme.SetStylebox("normal", "TextEdit", field);
        theme.SetStylebox("focus", "TextEdit", Slice(BoxImage(12, 12, SunkenFocus), 3, 5, 4));
        theme.SetColor("font_color", "TextEdit", UiPalette.Ink);

        // Check boxes: a sunken box, with the original green tick when on.
        Image off = Blank(16, 16);
        Box(off, new Rect2I(1, 2, 13, 13), Sunken);
        Image on = (Image)off.Duplicate();
        on.BlendRect(tick, new Rect2I(Vector2I.Zero, tick.GetSize()), new Vector2I(16 - tick.GetWidth(), 0));
        Texture2D offTexture = ImageTexture.CreateFromImage(off);
        Texture2D onTexture = ImageTexture.CreateFromImage(on);
        Texture2D offDisabled = ImageTexture.CreateFromImage(Faded(off, UiPalette.ParchmentMid));
        Texture2D onDisabled = ImageTexture.CreateFromImage(Faded(on, UiPalette.ParchmentMid));
        foreach (string type in new[] { "CheckBox", "CheckButton" })
        {
            theme.SetIcon("checked", type, onTexture);
            theme.SetIcon("unchecked", type, offTexture);
            theme.SetIcon("checked_disabled", type, onDisabled);
            theme.SetIcon("unchecked_disabled", type, offDisabled);
            theme.SetIcon("radio_checked", type, onTexture);
            theme.SetIcon("radio_unchecked", type, offTexture);
            theme.SetIcon("checked_mirrored", type, onTexture);
            theme.SetIcon("unchecked_mirrored", type, offTexture);
            theme.SetStylebox("normal", type, Empty(2, 3));
            theme.SetStylebox("hover", type, Slice(BoxImage(12, 12, Band), 3, 2, 3));
            theme.SetStylebox("pressed", type, Empty(2, 3));
            theme.SetStylebox("hover_pressed", type, Slice(BoxImage(12, 12, Band), 3, 2, 3));
            theme.SetStylebox("disabled", type, Empty(2, 3));
            theme.SetStylebox("focus", type, Empty());
            theme.SetColor("font_color", type, UiPalette.Ink);
            theme.SetColor("font_hover_color", type, UiPalette.Ink);
            theme.SetColor("font_pressed_color", type, UiPalette.Ink);
            theme.SetColor("font_hover_pressed_color", type, UiPalette.Ink);
            theme.SetColor("font_focus_color", type, UiPalette.Ink);
            theme.SetColor("font_disabled_color", type, UiPalette.ParchmentShadow);
            theme.SetConstant("h_separation", type, 6);
        }
        theme.SetIcon("on", "CheckButton", onTexture);
        theme.SetIcon("off", "CheckButton", offTexture);
        theme.SetIcon("on_disabled", "CheckButton", onDisabled);
        theme.SetIcon("off_disabled", "CheckButton", offDisabled);
        theme.SetIcon("on_mirrored", "CheckButton", onTexture);
        theme.SetIcon("off_mirrored", "CheckButton", offTexture);

        // Popup menus (option buttons): a light bubble, the menu arrow on the chosen entry.
        theme.SetStylebox("panel", "PopupMenu", Slice(BoxImage(12, 12, new BoxStyle(UiPalette.Ink, UiPalette.ParchmentLight, UiPalette.ParchmentPale, UiPalette.ParchmentMid, new Color(UiPalette.Ink, 0.5f))), 3, 3, 3, 4));
        theme.SetStylebox("hover", "PopupMenu", Slice(BoxImage(12, 12, BandDark), 3, 4, 2));
        theme.SetColor("font_color", "PopupMenu", UiPalette.Ink);
        theme.SetColor("font_hover_color", "PopupMenu", UiPalette.Ink);
        theme.SetColor("font_disabled_color", "PopupMenu", UiPalette.ParchmentShadow);
        theme.SetConstant("v_separation", "PopupMenu", 8);
        theme.SetConstant("h_separation", "PopupMenu", 6);
        theme.SetConstant("item_start_padding", "PopupMenu", 4);
        theme.SetConstant("item_end_padding", "PopupMenu", 6);
        theme.SetIcon("radio_checked", "PopupMenu", RightArrowIcon());
        theme.SetIcon("radio_unchecked", "PopupMenu", ImageTexture.CreateFromImage(Blank(8, 9)));
        theme.SetIcon("checked", "PopupMenu", RightArrowIcon());
        theme.SetIcon("unchecked", "PopupMenu", ImageTexture.CreateFromImage(Blank(8, 9)));

        // Progress bars: a groove filled with the green of full health bars.
        theme.SetStylebox("background", "ProgressBar", Slice(BoxImage(10, 10, Groove), 3, 2, 2));
        theme.SetStylebox("fill", "ProgressBar", Slice(BoxImage(8, 8, Bar), 2, 0, 0));
        theme.SetColor("font_color", "ProgressBar", UiPalette.Paper);
        theme.SetColor("font_outline_color", "ProgressBar", UiPalette.Ink);
        theme.SetConstant("outline_size", "ProgressBar", 2);
    }

    /// <summary>A small yellow arrow pointing right, like the original menu arrows.</summary>
    private static Texture2D RightArrowIcon()
    {
        Image image = Blank(8, 9);
        for (int row = 0; row < 4; row++)
        {
            Fill(image, new Rect2I(1, 1 + row, row + 1, 1), UiPalette.Yellow);
            Fill(image, new Rect2I(1, 7 - row, row + 1, 1), UiPalette.Yellow);
            Pixel(image, row + 2, 1 + row, UiPalette.Ink);
            Pixel(image, row + 2, 7 - row, UiPalette.Ink);
        }
        Fill(image, new Rect2I(1, 4, 5, 1), UiPalette.Yellow);
        Pixel(image, 6, 4, UiPalette.Ink);
        Fill(image, new Rect2I(0, 0, 1, 9), UiPalette.Ink);
        Pixel(image, 1, 0, UiPalette.Ink);
        Pixel(image, 1, 8, UiPalette.Ink);
        return ImageTexture.CreateFromImage(image);
    }

    // ---- lists and tabs -----------------------------------------------------------------------

    private static void AddLists(Theme theme)
    {
        theme.SetStylebox("panel", "ItemList", Slice(BoxImage(12, 12, Sunken), 3, 3, 3));
        theme.SetStylebox("focus", "ItemList", Empty());
        theme.SetStylebox("selected", "ItemList", Slice(BoxImage(12, 12, Pushed), 3, 2, 2));
        theme.SetStylebox("selected_focus", "ItemList", Slice(BoxImage(12, 12, Pushed), 3, 2, 2));
        theme.SetStylebox("hovered", "ItemList", Slice(BoxImage(12, 12, Band), 3, 2, 2));
        theme.SetStylebox("cursor", "ItemList", Empty());
        theme.SetStylebox("cursor_unfocused", "ItemList", Empty());
        theme.SetColor("font_color", "ItemList", UiPalette.Ink);
        theme.SetColor("font_selected_color", "ItemList", UiPalette.Ink);
        theme.SetColor("font_hovered_color", "ItemList", UiPalette.Ink);
        theme.SetConstant("v_separation", "ItemList", 6);
        theme.SetStylebox("panel", "ScrollContainer", Empty());
    }

    private static void AddTabs(Theme theme)
    {
        BoxStyle selectedStyle = new(UiPalette.Brown, UiPalette.Parchment, UiPalette.ParchmentLight, UiPalette.Parchment, UiPalette.Clear);
        Image selected = Blank(17, 13);
        Box(selected, new Rect2I(0, 0, 16, 15), selectedStyle);
        Image unselected = Blank(17, 13);
        Box(unselected, new Rect2I(0, 2, 16, 11), new BoxStyle(UiPalette.Brown, UiPalette.ParchmentDark, UiPalette.ParchmentMid, UiPalette.ParchmentDark, UiPalette.Clear));
        Image hovered = Blank(17, 13);
        Box(hovered, new Rect2I(0, 1, 16, 12), new BoxStyle(UiPalette.Brown, UiPalette.ParchmentMid, UiPalette.ParchmentLight, UiPalette.ParchmentDark, UiPalette.Clear));
        StyleBoxTexture Tab(Image image, int top) => new()
        {
            Texture = ImageTexture.CreateFromImage(image),
            TextureMarginLeft = 4,
            TextureMarginRight = 5,
            TextureMarginTop = 4,
            TextureMarginBottom = 2,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = top,
            ContentMarginBottom = 4,
        };
        foreach (string type in new[] { "TabContainer", "TabBar" })
        {
            theme.SetStylebox("tab_selected", type, Tab(selected, 5));
            theme.SetStylebox("tab_unselected", type, Tab(unselected, 6));
            theme.SetStylebox("tab_hovered", type, Tab(hovered, 6));
            theme.SetStylebox("tab_disabled", type, Tab(unselected, 6));
            theme.SetStylebox("tab_focus", type, Empty());
            theme.SetColor("font_selected_color", type, UiPalette.Ink);
            theme.SetColor("font_unselected_color", type, UiPalette.Brown);
            theme.SetColor("font_hovered_color", type, UiPalette.Ink);
            theme.SetColor("font_disabled_color", type, UiPalette.ParchmentShadow);
            theme.SetConstant("side_margin", type, 6);
        }
        Image panel = Blank(17, 17);
        Box(panel, new Rect2I(0, 0, 16, 16), new BoxStyle(UiPalette.Brown, UiPalette.Parchment, UiPalette.ParchmentLight, UiPalette.ParchmentDark, UiPalette.Clear));
        theme.SetStylebox("panel", "TabContainer", Slice(panel, 4, 8, 8));
        theme.SetStylebox("tabbar_background", "TabContainer", Empty());
    }

    // ---- scroll bars and sliders --------------------------------------------------------------

    private static void AddScrollBars(Theme theme)
    {
        StyleBoxTexture track = Slice(BoxImage(8, 10, Groove), 3, 1, 1);
        StyleBoxTexture grabber = Slice(BoxImage(8, 10, Raised with { Shadow = UiPalette.Clear }), 3, 4, 4);
        StyleBoxTexture grabberHover = Slice(BoxImage(8, 10, RaisedHover with { Shadow = UiPalette.Clear }), 3, 4, 4);
        StyleBoxTexture grabberPressed = Slice(BoxImage(8, 10, Pushed), 3, 4, 4);
        foreach (string type in new[] { "VScrollBar", "HScrollBar" })
        {
            theme.SetStylebox("scroll", type, track);
            theme.SetStylebox("scroll_focus", type, track);
            theme.SetStylebox("grabber", type, grabber);
            theme.SetStylebox("grabber_highlight", type, grabberHover);
            theme.SetStylebox("grabber_pressed", type, grabberPressed);
            Texture2D none = ImageTexture.CreateFromImage(Blank(1, 1));
            foreach (string icon in new[] { "increment", "increment_highlight", "increment_pressed", "decrement", "decrement_highlight", "decrement_pressed" })
            {
                theme.SetIcon(icon, type, none);
            }
        }
    }

    private static void AddSliders(Theme theme)
    {
        Image knob = Blank(12, 17);
        Box(knob, new Rect2I(0, 0, 11, 16), new BoxStyle(UiPalette.Ink, UiPalette.ParchmentLight, UiPalette.ParchmentPale, UiPalette.ParchmentDark, new Color(UiPalette.Ink, 0.6f)));
        for (int row = 5; row <= 10; row += 2)
        {
            Fill(knob, new Rect2I(3, row, 5, 1), UiPalette.Brown);
        }
        Image knobHover = Tinted(knob, new Color(1.08f, 1.08f, 1.08f));
        Image knobDisabled = Faded(knob, UiPalette.ParchmentMid);
        StyleBoxTexture groove = Slice(BoxImage(10, 8, Groove), 3, 0, 3);
        StyleBoxTexture filled = Slice(BoxImage(10, 8, Gold with { Shadow = UiPalette.Clear }), 3, 0, 3);
        foreach (string type in new[] { "HSlider", "VSlider" })
        {
            theme.SetStylebox("slider", type, groove);
            theme.SetStylebox("grabber_area", type, filled);
            theme.SetStylebox("grabber_area_highlight", type, filled);
            theme.SetIcon("grabber", type, ImageTexture.CreateFromImage(knob));
            theme.SetIcon("grabber_highlight", type, ImageTexture.CreateFromImage(knobHover));
            theme.SetIcon("grabber_disabled", type, ImageTexture.CreateFromImage(knobDisabled));
            theme.SetIcon("tick", type, ImageTexture.CreateFromImage(Blank(1, 1)));
            theme.SetConstant("center_grabber", type, 0);
            theme.SetConstant("grabber_offset", type, 0);
        }
    }
}
