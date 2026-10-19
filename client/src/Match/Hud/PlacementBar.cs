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

    /// <summary>The height the bar needs (at least what its frame and buttons take, so it never grows over the bars below it); first rebuilds the buttons if their size changed.</summary>
    public float WantedHeight
    {
        get
        {
            FitButtons();
            return Math.Max(Math.Max(PixelTheme.RowHeight, actions.Metrics.Button) + 8, GetCombinedMinimumSize().Y);
        }
    }

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
        AddChild(row);
        this.row = row;
        FitButtons();
        Visible = false;
    }

    private HBoxContainer row = null!;
    private int buttonSize;

    /// <summary>Builds the tick and the cross (again when the button size changed, as after turning the screen).</summary>
    private void FitButtons()
    {
        HudMetrics m = actions.Metrics;
        if (buttonSize == m.Button || row is null)
        {
            return;
        }
        bool disabled = false, shown = true;
        if (buttonSize > 0)
        {
            disabled = confirm.Disabled;
            shown = confirm.Visible;
            // Out of the row at once, so the new buttons get the same names (scripts press them by name).
            foreach (Button old in new[] { confirm, cancel })
            {
                row.RemoveChild(old);
                old.QueueFree();
            }
        }
        confirm = HudWidgets.IconButton(m, PixelTheme.TickIcon, "hud.place.confirm", () => Confirmed?.Invoke(), name: "ConfirmButton");
        confirm.Disabled = disabled;
        confirm.Visible = shown;
        row.AddChild(confirm);
        cancel = HudWidgets.IconButton(m, PixelTheme.CrossIcon, "common.cancel", () => Cancelled?.Invoke(), name: "CancelButton");
        row.AddChild(cancel);
        buttonSize = m.Button;
    }

    /// <summary>Shows the bar with <paramref name="text"/>; <paramref name="showConfirm"/> shows the tick (touch).</summary>
    public void Begin(string text, bool showConfirm)
    {
        Active = true;
        FitButtons();
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
