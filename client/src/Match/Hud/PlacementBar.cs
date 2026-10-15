using System;
using Godot;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The bar shown while placing a building or trap or picking a target: what to do (the
/// original's prompt), and the buttons to confirm (with a finger the ghost is moved by tapping
/// and placed with the tick) and to cancel.
/// </summary>
public partial class PlacementBar : PanelContainer
{
    private readonly IMatchActions actions;
    private Label prompt = null!;
    private Button confirm = null!;
    private Button cancel = null!;

    /// <summary>A bar for the match <paramref name="actions"/> plays.</summary>
    public PlacementBar(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The player confirmed (placed the ghost where it is).</summary>
    public event Action? Confirmed;

    /// <summary>The player cancelled.</summary>
    public event Action? Cancelled;

    /// <summary>Whether a placement or pick is going on.</summary>
    public bool Active { get; private set; }

    /// <summary>The height the bar needs.</summary>
    public float WantedHeight => Math.Max(PixelTheme.RowHeight, actions.Metrics.Button) + 8;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "PlacementBar";
        ThemeTypeVariation = PixelTheme.Variations.Sheet;
        MouseFilter = MouseFilterEnum.Stop;
        HBoxContainer row = HudPanels.Row(4);
        prompt = HudWidgets.Value("");
        prompt.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        prompt.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        prompt.CustomMinimumSize = new Vector2(60, 0);
        prompt.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        row.AddChild(prompt);
        confirm = HudWidgets.IconButton(actions.Metrics, PixelTheme.TickIcon, "hud.place.confirm", () => Confirmed?.Invoke(), name: "ConfirmButton");
        row.AddChild(confirm);
        cancel = HudWidgets.IconButton(actions.Metrics, PixelTheme.CrossIcon, "common.cancel", () => Cancelled?.Invoke(), name: "CancelButton");
        row.AddChild(cancel);
        AddChild(row);
        Visible = false;
    }

    /// <summary>Shows the bar with <paramref name="text"/>; <paramref name="showConfirm"/> shows the tick (touch).</summary>
    public void Begin(string text, bool showConfirm)
    {
        Active = true;
        prompt.Text = text;
        confirm.Visible = showConfirm;
        Visible = true;
    }

    /// <summary>Whether the tick is enabled (the ghost may be placed where it is).</summary>
    public void SetConfirmable(bool canConfirm) => confirm.Disabled = !canConfirm;

    /// <summary>Hides the bar.</summary>
    public void End()
    {
        Active = false;
        Visible = false;
    }
}
