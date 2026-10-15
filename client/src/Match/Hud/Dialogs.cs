using System;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;
using Tribes.Core.Commands;
using Tribes.Core.Maps;
using Tribes.Core.Systems.Notices;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// A hint of the rules, as the original's hint screen: its text with the face and name of the
/// villager it is about. The hungry and tired villager's pleas (hints 14 and 15) can be answered
/// by sending it to eat or rest; hints 4 and 5 can show the place they are about. Hints can be
/// switched off from here (the chief's death and the hints after it always show). Holds the
/// game while open when playing alone.
/// </summary>
public partial class HintDialog : HudPopup
{
    private const int ShowsPlaceFirst = 4, ShowsPlaceLast = 5, HungryPlea = 14, TiredPlea = 15, AlwaysShownFrom = 18;

    /// <summary>The hint of an enemy in sight: it points at the enemy by its cell code.</summary>
    private const int EnemySighted = 4;
    private readonly IMatchActions actions;
    private readonly HintEvent hint;

    /// <summary>The dialog of <paramref name="hint"/>.</summary>
    public HintDialog(IMatchActions actions, HintEvent hint)
        : base("hud.hint.title", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)), HoldReason.Hint)
    {
        this.actions = actions;
        this.hint = hint ?? throw new ArgumentNullException(nameof(hint));
        Name = "HintDialog";
        MaxWidth = 380;
        Closable = false;
    }

    /// <summary>The hint shown.</summary>
    public HintEvent Hint => hint;

    /// <summary>The player asked to see the place the hint is about (map cell).</summary>
    public event Action<int, int>? ShowPlace;

    /// <inheritdoc/>
    protected override void Fill()
    {
        GameWorld world = actions.World;
        HBoxContainer row = HudPanels.Row(6);
        if (hint.Subject >= 1 && hint.Subject <= world.UnitSlots && world.Unit(hint.Subject).HitPoints > 0)
        {
            Unit unit = world.Unit(hint.Subject);
            VBoxContainer who = HudPanels.Column(1);
            who.AddChild(HudPanels.Framed(HudArt.Icon(HudArt.Face(unit.Portrait, actions.ColourOf(unit.Tribe)))));
            who.AddChild(HudWidgets.Value(GameTexts.UnitName(unit), PixelTheme.Variations.Muted));
            row.AddChild(who);
        }
        Label text = HudWidgets.Text(GameTexts.Hint(hint.Hint), translate: false);
        text.Name = "HintText";
        text.CustomMinimumSize = new Vector2(160, 0);
        row.AddChild(text);
        Content.AddChild(row);
        HFlowContainer buttons = Footer;
        if (hint.Hint is HungryPlea or TiredPlea && !actions.Spectating)
        {
            UnitNeed need = hint.Hint == HungryPlea ? UnitNeed.Food : UnitNeed.Rest;
            buttons.AddChild(HudWidgets.TextButton(Metrics, HudArt.Painted(need == UnitNeed.Food ? PaintedIcon.Eat : PaintedIcon.Rest),
                need == UnitNeed.Food ? "hud.order.eat" : "hud.order.rest", () =>
                {
                    actions.Send(new TendNeed(actions.Tribe, hint.Subject, need));
                    Close();
                }, "YesButton", PixelTheme.Variations.Primary));
            buttons.AddChild(HudWidgets.TextButton(Metrics, PixelTheme.CrossIcon, "common.no", Close, "NoButton"));
        }
        else if (hint.Hint is >= ShowsPlaceFirst and <= ShowsPlaceLast && Place(world) is { } place)
        {
            buttons.AddChild(HudWidgets.TextButton(Metrics, HudArt.Painted(PaintedIcon.Centre), "hud.hint.show", () =>
            {
                Close();
                ShowPlace?.Invoke(place.X, place.Y);
            }, "YesButton", PixelTheme.Variations.Primary));
            buttons.AddChild(HudWidgets.TextButton(Metrics, PixelTheme.CrossIcon, "common.no", Close, "NoButton"));
        }
        else
        {
            buttons.AddChild(HudWidgets.TextButton(Metrics, PixelTheme.TickIcon, "common.ok", Close, "OkButton", PixelTheme.Variations.Primary));
        }
        foreach (Node child in buttons.GetChildren())
        {
            ((Control)child).SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            if (child is Button button)
            {
                // Footer buttons are as wide as their words.
                button.ClipText = false;
            }
        }
        if (hint.Hint < AlwaysShownFrom && !actions.Spectating)
        {
            var off = new CheckBox { Name = "HintsOff", Text = "hud.hint.off", FocusMode = FocusModeEnum.None };
            off.Toggled += on => actions.Send(new SetHints(actions.Tribe, !on));
            Content.AddChild(off);
        }
    }

    /// <summary>The cell the hint points at (a unit or a cell code), if any.</summary>
    private (int X, int Y)? Place(GameWorld world)
    {
        int target = hint.Target;
        if (hint.Hint != EnemySighted)
        {
            // The other hints point at a unit by its slot.
            target = CellCode.OfUnit(target);
        }
        if (CellTargets.UnitOf(world, target) is int unit)
        {
            return (world.Unit(unit).X, world.Unit(unit).Y);
        }
        if (CellTargets.AnimalOf(world, target) is int slot)
        {
            Animal animal = world.Animal(slot);
            return (animal.X, animal.Y);
        }
        if (CellTargets.BuildingOf(world, target) is int building)
        {
            return (world.Building(building).X, world.Building(building).Y);
        }
        return hint.Subject >= 1 && hint.Subject <= world.UnitSlots ? (world.Unit(hint.Subject).X, world.Unit(hint.Subject).Y) : null;
    }
}

/// <summary>What the result screen lets the player do next.</summary>
public enum ResultChoice
{
    /// <summary>On to the next mission of the campaign straight away (as the original goes on).</summary>
    Next,

    /// <summary>Back to the campaign's mission list (the next mission is open after a victory).</summary>
    Continue,

    /// <summary>Play the mission again.</summary>
    Retry,

    /// <summary>Back to the main menu.</summary>
    MainMenu,

    /// <summary>The campaign is won: its closing story plays in this world (the only way on, as in the original).</summary>
    Ending,
}

/// <summary>
/// The end of a mission as the original shows it: the victory or defeat picture and words, then
/// the score sheet (births, buildings built, enemy buildings razed, enemies killed, resources
/// gathered and the score) of every tribe.
/// </summary>
public partial class ResultsPanel : HudPopup
{
    private const int VictoryText = 57, DefeatText = 56, PlayAgain = 58, VictoryPicture = 10, DefeatPicture = 11;
    private static readonly (TribeCounter Counter, string Key)[] Rows =
    {
        (TribeCounter.Born, "hud.results.born"),
        (TribeCounter.Built, "hud.results.built"),
        (TribeCounter.BuildingsDestroyed, "hud.results.razed"),
        (TribeCounter.UnitsKilled, "hud.results.killed"),
        (TribeCounter.Gathered, "hud.results.gathered"),
        (TribeCounter.Score, "hud.results.score"),
    };

    /// <summary>Height of the result picture: smaller on short screens so the score sheet fits.</summary>
    private float PictureHeight => Mathf.Clamp(Metrics.Screen.Y * (Metrics.Screen.X > Metrics.Screen.Y ? 0.4f : 0.22f), 56, 150);

    private readonly IMatchActions actions;
    private readonly bool victory;
    private readonly bool campaign;
    private readonly bool hasNext;
    private readonly bool endsCampaign;

    /// <summary>
    /// The result screen; <paramref name="campaign"/> offers going back to the campaign,
    /// <paramref name="hasNext"/> going on to the next mission, <paramref name="endsCampaign"/>
    /// (the campaign's last mission won) only going on to the closing story.
    /// </summary>
    public ResultsPanel(IMatchActions actions, bool victory, bool campaign, bool hasNext, bool endsCampaign = false)
        : base("s.97", actions?.Metrics ?? throw new ArgumentNullException(nameof(actions)), HoldReason.Results)
    {
        this.actions = actions;
        this.victory = victory;
        this.campaign = campaign;
        this.hasNext = hasNext;
        this.endsCampaign = endsCampaign;
        Name = "ResultsPanel";
        MaxWidth = 560;
        Closable = false;
    }

    /// <summary>The player chose what comes next.</summary>
    public event Action<ResultChoice>? Chosen;

    /// <inheritdoc/>
    protected override void Fill()
    {
        GameWorld world = actions.World;
        // Landscape: the picture and words beside the score sheet; portrait: one under the other.
        bool wide = Metrics.Screen.X > Metrics.Screen.Y;
        BoxContainer layout = wide ? HudPanels.Row(10) : HudPanels.Column(4);
        Content.AddChild(layout);
        VBoxContainer left = HudPanels.Column(4);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        layout.AddChild(left);
        VBoxContainer right = wide ? HudPanels.Column(4) : left;
        if (wide)
        {
            right.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            layout.AddChild(right);
        }
        string path = $"res://assets/original/pi8_{(victory ? VictoryPicture : DefeatPicture):00}.png";
        if (ResourceLoader.Exists(path))
        {
            left.AddChild(new TextureRect
            {
                Name = "ResultPicture",
                Texture = GD.Load<Texture2D>(path),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(0, PictureHeight),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        Label title = HudWidgets.Value(GameTexts.T(victory ? VictoryText : DefeatText), PixelTheme.Variations.Title);
        title.Name = "ResultTitle";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        left.AddChild(title);
        right.AddChild(ScoreSheet(world));
        if (!victory && !actions.Runner.IsNetwork)
        {
            right.AddChild(HudWidgets.Text(GameTexts.T(PlayAgain), translate: false));
        }
        HFlowContainer buttons = Footer;
        if (campaign && victory && endsCampaign)
        {
            // The original goes from this result straight into the closing story.
            buttons.AddChild(Choice("hud.results.ending", ResultChoice.Ending, primary: true));
            return;
        }
        if (campaign && victory && hasNext)
        {
            buttons.AddChild(Choice("hud.results.next", ResultChoice.Next, primary: true));
        }
        if (campaign)
        {
            buttons.AddChild(Choice("hud.results.continue", ResultChoice.Continue, primary: victory && !hasNext));
        }
        if (!victory && !actions.Runner.IsNetwork)
        {
            buttons.AddChild(Choice("hud.results.retry", ResultChoice.Retry, primary: true));
        }
        buttons.AddChild(Choice("hud.results.menu", ResultChoice.MainMenu, primary: !campaign && victory));
    }

    private Control ScoreSheet(GameWorld world)
    {
        var grid = new GridContainer { Name = "ScoreSheet", Columns = world.Tribes.Count + 1, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore });
        for (int t = 0; t < world.Tribes.Count; t++)
        {
            Label name = HudWidgets.Value(PlayersStrip.TribeName(actions, t), PixelTheme.Variations.Heading);
            name.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(name);
        }
        foreach ((TribeCounter counter, string key) in Rows)
        {
            grid.AddChild(HudWidgets.Value(UiText.T(key)));
            for (int t = 0; t < world.Tribes.Count; t++)
            {
                int value = world.Tribes[t][counter];
                Label cell = HudWidgets.Value((counter == TribeCounter.Score ? value * 10 : value).ToString(CultureInfo.InvariantCulture));
                cell.HorizontalAlignment = HorizontalAlignment.Right;
                grid.AddChild(cell);
            }
        }
        return grid;
    }

    private Button Choice(string key, ResultChoice choice, bool primary)
    {
        Button button = HudWidgets.TextButton(Metrics, null, key, () => Chosen?.Invoke(choice), choice + "Button",
            primary ? PixelTheme.Variations.Primary : null);
        button.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        button.Alignment = HorizontalAlignment.Center;
        button.ClipText = false;
        button.CustomMinimumSize = new Vector2(96, button.CustomMinimumSize.Y);
        return button;
    }
}
