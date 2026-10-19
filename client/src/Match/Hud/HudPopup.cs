using System;
using Godot;
using Tribes.Client.Ui;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// A panel opened over the map (build menu, roster, army, help, menus...): a parchment sheet with
/// a title and a close button, centred on the free part of the screen and never larger than the
/// screen; its content scrolls when it does not fit. Esc, the Android back key and the close
/// button close it.
/// </summary>
public partial class HudPopup : PanelContainer
{
    private const int Margin = 6;

    private readonly string titleKey;
    private VBoxContainer body = null!;
    private Label title = null!;
    private ScrollContainer scroll = null!;

    /// <summary>A popup titled <paramref name="titleKey"/>.</summary>
    public HudPopup(string titleKey, HudMetrics metrics, HoldReason? hold = HoldReason.Panel)
    {
        this.titleKey = titleKey;
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        Hold = hold;
    }

    /// <summary>The interface sizes it was built for.</summary>
    public HudMetrics Metrics { get; }

    /// <summary>Why the game is held while it is open; null when it is not.</summary>
    public HoldReason? Hold { get; }

    /// <summary>Where the content goes (it scrolls when it does not fit).</summary>
    public VBoxContainer Content { get; private set; } = null!;

    /// <summary>The list <see cref="Content"/> scrolls in.</summary>
    protected ScrollContainer ContentScroll => scroll;

    /// <summary>A row under the content that never scrolls away (the buttons of dialogs).</summary>
    public HFlowContainer Footer { get; private set; } = null!;

    /// <summary>Widest it gets, in logical pixels.</summary>
    public float MaxWidth { get; set; } = 360;

    /// <summary>Whether a close button is shown.</summary>
    public bool Closable { get; set; } = true;

    /// <summary>The popup closed.</summary>
    public event Action? Closed;

    /// <inheritdoc/>
    public override void _Ready()
    {
        ThemeTypeVariation = PixelTheme.Variations.Sheet;
        MouseFilter = MouseFilterEnum.Stop;
        body = HudPanels.Column(4);
        AddChild(body);
        HBoxContainer header = HudPanels.Row(4);
        title = new Label
        {
            Text = titleKey,
            ThemeTypeVariation = PixelTheme.Variations.Title,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        header.AddChild(title);
        if (Closable)
        {
            Button close = HudWidgets.IconButton(Metrics, HudArt.Painted(PaintedIcon.Close), "common.back", Close, name: "CloseButton");
            header.AddChild(close);
        }
        body.AddChild(header);
        body.AddChild(HudPanels.Separator());
        Content = HudPanels.Column(4);
        Content.Name = "Content";
        scroll = UiFactory.Scroll(Content);
        scroll.MouseFilter = MouseFilterEnum.Pass;
        body.AddChild(scroll);
        Footer = HudPanels.Flow(Metrics.Gap);
        Footer.Name = "Footer";
        Footer.Alignment = FlowContainer.AlignmentMode.End;
        body.AddChild(Footer);
        Fill();
        Footer.Visible = Footer.GetChildCount() > 0;
    }

    /// <summary>Builds the content (called once when the popup enters the tree).</summary>
    protected virtual void Fill()
    {
    }

    /// <summary>Rebuilds the content.</summary>
    public void Refill()
    {
        HudWidgets.Clear(Content);
        HudWidgets.Clear(Footer);
        Fill();
        Footer.Visible = Footer.GetChildCount() > 0;
    }

    /// <summary>Places the popup in the middle of <paramref name="area"/>.</summary>
    public void Place(Rect2 area)
    {
        float width = Mathf.Min(MaxWidth, area.Size.X - (2 * Margin));
        float maxHeight = area.Size.Y - (2 * Margin);
        scroll.CustomMinimumSize = Vector2.Zero;
        Size = new Vector2(width, 0);
        Vector2 wanted = GetCombinedMinimumSize();
        float contentHeight = Content.GetCombinedMinimumSize().Y;
        float chrome = wanted.Y - scroll.GetCombinedMinimumSize().Y;
        float height = Mathf.Min(maxHeight, chrome + contentHeight);
        scroll.CustomMinimumSize = new Vector2(0, Mathf.Max(0, height - chrome));
        Size = new Vector2(width, height);
        Position = (area.Position + ((area.Size - Size) / 2)).Floor();
    }

    /// <summary>Closes the popup.</summary>
    public void Close()
    {
        if (!IsInsideTree())
        {
            return;
        }
        Closed?.Invoke();
        QueueFree();
        GetParent()?.RemoveChild(this);
    }
}
