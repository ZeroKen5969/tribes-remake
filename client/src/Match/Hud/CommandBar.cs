using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;

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
/// and objective (the menu and speed are on the top bar). All of them always fit on the screen
/// (<see cref="ButtonStrip"/>): a little smaller when the edge is short, in two lines when even
/// that is too long. Each button is named after its command (for scripted checks).
/// </summary>
public partial class CommandBar : BoxContainer
{
    private const int BuildEntry = 10, VillagersEntry = 13, AllOutEntry = 12;

    private readonly IMatchActions actions;
    private readonly Dictionary<HudCommand, Button> buttons = new();
    private string arranged = "";

    /// <summary>A bar for the match <paramref name="actions"/> plays.</summary>
    public CommandBar(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>A button was pressed.</summary>
    public event Action<HudCommand>? Pressed;

    /// <summary>How deep the bar is across its edge (its width as a column, its height as a row).</summary>
    public float Thickness { get; private set; }

    /// <summary>The button of <paramref name="command"/>, if shown.</summary>
    public Button? ButtonOf(HudCommand command) => buttons.TryGetValue(command, out Button? b) ? b : null;

    /// <summary>
    /// Puts the buttons for <paramref name="metrics"/> in a column (<paramref name="vertical"/>) or
    /// a row <paramref name="length"/> pixels long. Nothing is rebuilt when nothing changed, unless
    /// <paramref name="force"/>.
    /// </summary>
    public void Arrange(HudMetrics metrics, bool vertical, float length, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        List<(HudCommand Command, Texture2D Icon, string Tip, bool Toggle)> wanted = Wanted(metrics);
        StripFit fit = ButtonStrip.Fit(wanted.Count, length, metrics.Button, metrics.SmallestButton, metrics.Gap);
        string key = string.Join(",", vertical, fit, metrics.Gap, metrics.Touch, string.Join("/", wanted.ConvertAll(w => w.Command)));
        if (!force && key == arranged)
        {
            return;
        }
        arranged = key;
        Name = "CommandBar";
        // The lines lie side by side across the edge; each runs along it.
        Vertical = !vertical;
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
        HudMetrics sized = metrics with { Button = fit.Button, IconScale = HudMetrics.IconScaleFor(fit.Button) };
        BoxContainer? line = null;
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i % Math.Max(1, fit.PerLine) == 0)
            {
                line = new BoxContainer
                {
                    Name = "Line" + (i / Math.Max(1, fit.PerLine)).ToString(CultureInfo.InvariantCulture),
                    Vertical = vertical,
                    Alignment = AlignmentMode.Center,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                line.AddThemeConstantOverride("separation", metrics.Gap);
                AddChild(line);
            }
            (HudCommand command, Texture2D icon, string tip, bool toggle) = wanted[i];
            HudCommand pressed = command;
            Button button = HudWidgets.IconButton(sized, icon, tip, () => Pressed?.Invoke(pressed), name: command + "Button");
            button.ToggleMode = toggle;
            buttons[command] = button;
            line!.AddChild(button);
        }
        Thickness = fit.Thickness(metrics.Gap);
        foreach (HudCommand command in wasOn)
        {
            SetToggled(command, true);
        }
    }

    /// <summary>The buttons the bar shows now, in order.</summary>
    private List<(HudCommand, Texture2D, string, bool)> Wanted(HudMetrics metrics)
    {
        var wanted = new List<(HudCommand, Texture2D, string, bool)>();
        bool orders = !actions.Spectating;
        if (metrics.Touch)
        {
            wanted.Add((HudCommand.Menu, HudArt.Painted(PaintedIcon.Menu), "hud.menu", false));
            if (actions.Runner.CanPause)
            {
                wanted.Add((HudCommand.Pause, HudArt.Painted(PaintedIcon.Pause), "hud.pause", true));
            }
        }
        if (orders)
        {
            wanted.Add((HudCommand.Build, HudArt.Effect(BuildEntry), GameTexts.Entry(BuildEntry), false));
            wanted.Add((HudCommand.Army, HudArt.Effect(AllOutEntry), "hud.army", false));
            wanted.Add((HudCommand.Roster, HudArt.Effect(VillagersEntry), GameTexts.Entry(VillagersEntry), false));
        }
        if (actions.World.Endless is not null)
        {
            // Endless worlds: the tribes and alliances (also for watchers, who only read it).
            wanted.Add((HudCommand.Relations, HudArt.Painted(PaintedIcon.Alliance), "relations.title", false));
        }
        if (metrics.Touch)
        {
            if (orders)
            {
                wanted.Add((HudCommand.MultiSelect, HudArt.Painted(PaintedIcon.MultiSelect), "hud.multiselect", true));
            }
            wanted.Add((HudCommand.Minimap, HudArt.Painted(PaintedIcon.Minimap), "hud.minimap", true));
        }
        else
        {
            wanted.Add((HudCommand.Centre, HudArt.Painted(PaintedIcon.Centre), "hud.order.centre", false));
            wanted.Add((HudCommand.Minimap, HudArt.Painted(PaintedIcon.Minimap), "hud.minimap", true));
            wanted.Add((HudCommand.Help, HudArt.Painted(PaintedIcon.Help), "hud.help", false));
            wanted.Add((HudCommand.Objective, HudArt.Painted(PaintedIcon.Objective), "hud.objective", false));
        }
        return wanted;
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
}
