using System;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Audio;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;

namespace Tribes.Client.Ui;

/// <summary>
/// Base of the menu screens: the title backdrop, the parchment sheet with a title and a back
/// button, portrait/landscape layout, live language changes, menu music and the back action
/// (Esc, Android back).
/// </summary>
public abstract partial class ScreenBase : Control
{
    /// <summary>Widest the sheet gets on large screens.</summary>
    protected const int MaxSheetWidth = 720;

    private MarginContainer? sheetMargin;
    private bool portrait;
    private bool laidOut;

    /// <summary>The screen shown now.</summary>
    public static ScreenBase? Current { get; private set; }

    /// <summary>Whether the screen shows what it is meant to show (development screenshots wait for it).</summary>
    public bool IsSettled { get; protected set; } = true;

    /// <summary>Whether the screen is taller than wide.</summary>
    public bool IsPortrait => portrait;

    /// <summary>The backdrop behind the sheet.</summary>
    protected TitleBackdrop Backdrop { get; private set; } = null!;

    /// <summary>Layer above everything for dialogs and notes.</summary>
    protected Control Overlay { get; private set; } = null!;

    /// <summary>The screen the back action goes to; null to stay.</summary>
    protected virtual string? BackScreen => UiScreens.MainMenu;

    /// <summary>Music mood of this screen.</summary>
    protected virtual string MusicMood => MusicPlayer.Normal;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Current = this;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        GetTree().Root.Theme = PixelTheme.Current;
        Theme = PixelTheme.Current;
        GetTree().QuitOnGoBack = false;
        Backdrop = new TitleBackdrop { Name = "Backdrop" };
        AddChild(Backdrop);
        Build();
        Overlay = new Control { Name = "Overlay", MouseFilter = MouseFilterEnum.Ignore };
        AddChild(Overlay);
        Overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Resized += UpdateLayout;
        GameApp.Instance.SafeMarginsChanged += OnSafeMarginsChanged;
        UiText.Changed += OnLanguageChanged;
        MusicPlayer.Instance?.PlayMood(MusicMood);
        CallDeferred(MethodName.UpdateLayout);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        UiText.Changed -= OnLanguageChanged;
        GameApp.Instance.SafeMarginsChanged -= OnSafeMarginsChanged;
        if (Current == this)
        {
            Current = null;
        }
    }

    /// <summary>Creates the screen's controls.</summary>
    protected abstract void Build();

    /// <summary>Arranges the controls for a portrait or landscape screen; called when the screen turns and when the room kept from its edges changes.</summary>
    protected virtual void Layout(bool isPortrait)
    {
    }

    /// <summary>Sets the texts the screen formats itself (keyed texts follow the language by themselves).</summary>
    protected virtual void RefreshTexts()
    {
    }

    private void OnLanguageChanged() => RefreshTexts();

    private void OnSafeMarginsChanged()
    {
        // Screens place things inside the room kept from the edges, so they lay out again.
        laidOut = false;
        CallDeferred(MethodName.UpdateLayout);
    }

    /// <summary>The room kept from each screen edge (cut-outs, rounded corners, the player's setting), in logical pixels.</summary>
    protected static Logic.EdgeMargins SafeMargins => GameApp.Instance.SafeMargins;

    private void UpdateLayout()
    {
        bool now = Size.Y > Size.X;
        if (sheetMargin is not null)
        {
            // The sheet keeps its own gap inside the room kept from the screen's edges.
            Logic.EdgeMargins safe = SafeMargins;
            int side = now ? 4 : 8;
            int extra = Math.Max(0, (int)(Size.X - MaxSheetWidth) / 2);
            sheetMargin.AddThemeConstantOverride("margin_left", Math.Max(side + extra, (int)safe.Left + side));
            sheetMargin.AddThemeConstantOverride("margin_right", Math.Max(side + extra, (int)safe.Right + side));
            sheetMargin.AddThemeConstantOverride("margin_top", (int)safe.Top + (now ? 6 : 4));
            sheetMargin.AddThemeConstantOverride("margin_bottom", (int)safe.Bottom + (now ? 6 : 4));
        }
        if (!laidOut || now != portrait)
        {
            portrait = now;
            laidOut = true;
            Layout(portrait);
        }
    }

    /// <summary>
    /// The standard screen: a parchment sheet filling the screen with the title
    /// <paramref name="titleKey"/> and a back button; returns the area for the content.
    /// </summary>
    protected VBoxContainer BuildSheet(string titleKey, out HBoxContainer header)
    {
        sheetMargin = new MarginContainer { Name = "SheetMargin" };
        AddChild(sheetMargin);
        sheetMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var sheet = new PanelContainer { Name = "Sheet", ThemeTypeVariation = PixelTheme.Variations.Sheet };
        sheetMargin.AddChild(sheet);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 4);
        sheet.AddChild(body);

        header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 6);
        body.AddChild(header);
        if (BackScreen is not null)
        {
            Button back = UiFactory.BackButton();
            back.Pressed += GoBack;
            header.AddChild(back);
        }
        var title = new Label
        {
            Text = titleKey,
            ThemeTypeVariation = PixelTheme.Variations.Title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
        };
        header.AddChild(title);
        if (BackScreen is not null)
        {
            // Keeps the title centred against the back button.
            header.AddChild(new Control { CustomMinimumSize = new Vector2(UiFactory.BackButtonWidth, 0) });
        }
        body.AddChild(new HSeparator());
        var content = new VBoxContainer { Name = "Content", SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 6);
        // A screen too small for its content (a large interface on a small screen) scrolls as a
        // page under its title, down and also sideways on a very narrow one; when everything
        // fits the page is just the content, as before.
        var page = new ScrollContainer
        {
            Name = "Page",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FollowFocus = true,
        };
        page.AddChild(content);
        body.AddChild(page);
        Callable.From(() =>
        {
            if (IsInstanceValid(content))
            {
                KeepListsOpen(content);
            }
        }).CallDeferred();
        return content;
    }

    /// <summary>Rows a list on a page keeps however little room the page has (it scrolls inside them).</summary>
    private const int ListMinimumHeight = 72;

    /// <summary>
    /// Gives the lists on the page a few rows' height, so on a page that scrolls they do not
    /// shrink to nothing (with room to spare they still take all the room they are given).
    /// </summary>
    private static void KeepListsOpen(Control content)
    {
        foreach (Node child in content.GetChildren())
        {
            if (child is ScrollContainer list && list.CustomMinimumSize.Y < ListMinimumHeight)
            {
                list.CustomMinimumSize = new Vector2(list.CustomMinimumSize.X, ListMinimumHeight);
            }
            if (child is Control control)
            {
                KeepListsOpen(control);
            }
        }
    }

    /// <summary>Goes to <see cref="BackScreen"/>.</summary>
    protected virtual void GoBack()
    {
        if (BackScreen is not null)
        {
            UiScreens.Go(BackScreen);
        }
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel") && !ModalDialog.IsOpen(Overlay))
        {
            GetViewport().SetInputAsHandled();
            GoBack();
        }
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest)
        {
            if (ModalDialog.IsOpen(Overlay))
            {
                ModalDialog.CloseTop(Overlay);
            }
            else
            {
                GoBack();
            }
        }
    }

    /// <summary>Shows a message in a dialog with an OK button.</summary>
    protected void ShowMessage(string text, Action? closed = null) =>
        ModalDialog.Show(Overlay, text, (UiKeys.Ok, closed));

    /// <summary>Shows a short note at the top of the screen for a few seconds.</summary>
    protected void ShowNote(string text) => Toast.Show(Overlay, text);

    /// <summary>The settings.</summary>
    protected static GameSettings Settings => GameApp.Instance.Settings;
}
