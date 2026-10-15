using System;
using System.Collections.Generic;
using Godot;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>The big buttons of the match.</summary>
public enum HudCommand
{
    /// <summary>The game menu.</summary>
    Menu,

    /// <summary>Pause or go on (single player).</summary>
    Pause,

    /// <summary>The build menu.</summary>
    Build,

    /// <summary>The army calls.</summary>
    Army,

    /// <summary>The unit roster.</summary>
    Roster,

    /// <summary>Touch: taps add units to the selection and a drag draws a box.</summary>
    MultiSelect,

    /// <summary>Show or hide the minimap.</summary>
    Minimap,

    /// <summary>Show the selection (or home).</summary>
    Centre,

    /// <summary>Help and keys.</summary>
    Help,

    /// <summary>The mission's objective.</summary>
    Objective,

    /// <summary>The tribes of an endless world and the alliances.</summary>
    Relations,
}

/// <summary>
/// The row (portrait) or column (landscape) of big buttons: with a finger the menu, pause, build,
/// army, roster, multi-select and minimap; with a mouse build, army, roster, centre, minimap, help
/// and objective (the menu and speed are on the top bar). Each button is named after its
/// command (for scripted checks).
/// </summary>
public partial class CommandBar : BoxContainer
{
    private const int BuildEntry = 10, VillagersEntry = 13, AllOutEntry = 12;

    private readonly IMatchActions actions;
    private readonly Dictionary<HudCommand, Button> buttons = new();

    /// <summary>A bar for the match <paramref name="actions"/> plays.</summary>
    public CommandBar(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>A button was pressed.</summary>
    public event Action<HudCommand>? Pressed;

    /// <summary>The button of <paramref name="command"/>, if shown.</summary>
    public Button? ButtonOf(HudCommand command) => buttons.TryGetValue(command, out Button? b) ? b : null;

    /// <summary>Puts the buttons for <paramref name="metrics"/> in a row or a column.</summary>
    public void Arrange(HudMetrics metrics, bool vertical)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        Name = "CommandBar";
        Vertical = vertical;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", metrics.Gap);
        Alignment = AlignmentMode.Center;
        // The new buttons keep the toggles that were on (multi-select, minimap).
        var wasOn = new List<HudCommand>();
        foreach ((HudCommand command, Button button) in buttons)
        {
            if (button.ToggleMode && button.ButtonPressed)
            {
                wasOn.Add(command);
            }
        }
        HudWidgets.Clear(this);
        buttons.Clear();
        bool orders = !actions.Spectating;
        if (metrics.Touch)
        {
            Add(metrics, HudCommand.Menu, HudArt.Painted(PaintedIcon.Menu), "hud.menu");
            if (actions.Runner.CanPause)
            {
                Add(metrics, HudCommand.Pause, HudArt.Painted(PaintedIcon.Pause), "hud.pause").ToggleMode = true;
            }
        }
        if (orders)
        {
            Add(metrics, HudCommand.Build, HudArt.Effect(BuildEntry), GameTexts.Entry(BuildEntry));
            Add(metrics, HudCommand.Army, HudArt.Effect(AllOutEntry), "hud.army");
            Add(metrics, HudCommand.Roster, HudArt.Effect(VillagersEntry), GameTexts.Entry(VillagersEntry));
        }
        if (actions.World.Endless is not null)
        {
            // Endless worlds: the tribes and alliances (also for watchers, who only read it).
            Add(metrics, HudCommand.Relations, HudArt.Painted(PaintedIcon.Alliance), "relations.title");
        }
        if (metrics.Touch)
        {
            if (orders)
            {
                Add(metrics, HudCommand.MultiSelect, HudArt.Painted(PaintedIcon.MultiSelect), "hud.multiselect").ToggleMode = true;
            }
            Add(metrics, HudCommand.Minimap, HudArt.Painted(PaintedIcon.Minimap), "hud.minimap").ToggleMode = true;
        }
        else
        {
            Add(metrics, HudCommand.Centre, HudArt.Painted(PaintedIcon.Centre), "hud.order.centre");
            Add(metrics, HudCommand.Minimap, HudArt.Painted(PaintedIcon.Minimap), "hud.minimap").ToggleMode = true;
            Add(metrics, HudCommand.Help, HudArt.Painted(PaintedIcon.Help), "hud.help");
            Add(metrics, HudCommand.Objective, HudArt.Painted(PaintedIcon.Objective), "hud.objective");
        }
        foreach (HudCommand command in wasOn)
        {
            SetToggled(command, true);
        }
    }

    /// <summary>Shows the state of the toggle buttons.</summary>
    public void SetToggled(HudCommand command, bool on)
    {
        if (buttons.TryGetValue(command, out Button? button) && button.ToggleMode)
        {
            button.SetPressedNoSignal(on);
        }
    }

    /// <summary>How many buttons the bar has.</summary>
    public int Count => buttons.Count;

    private Button Add(HudMetrics metrics, HudCommand command, Texture2D icon, string tip)
    {
        Button button = HudWidgets.IconButton(metrics, icon, tip, () => Pressed?.Invoke(command), name: command + "Button");
        buttons[command] = button;
        AddChild(button);
        return button;
    }
}
