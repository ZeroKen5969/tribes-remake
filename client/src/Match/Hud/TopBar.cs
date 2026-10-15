using System;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The strip across the top: the tribe's population and living space, food, wood, stones and
/// bones (the original's resource bar and icons), the army call in force, the game speed and
/// the menu (with a mouse; on touch screens they sit with the other big buttons).
/// </summary>
public partial class TopBar : PanelContainer
{
    private const int ArmyAllOutEffect = 30, ArmyRallyEffect = 31;

    private readonly IMatchActions actions;
    private readonly Label population = HudWidgets.Value("0");
    private readonly Label food = HudWidgets.Value("0");
    private readonly Label wood = HudWidgets.Value("0");
    private readonly Label stones = HudWidgets.Value("0");
    private readonly Label bones = HudWidgets.Value("0");
    private readonly Label weapons = HudWidgets.Value("0");
    private readonly TextureRect army = new() { MouseFilter = MouseFilterEnum.Ignore };
    private HBoxContainer speedBox = null!;
    private Button pause = null!;
    private Button[] speeds = Array.Empty<Button>();
    private Button menu = null!;
    private Button chief = null!;
    private int shownHash;

    /// <summary>A bar for the match <paramref name="actions"/> plays.</summary>
    public TopBar(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The menu button was pressed.</summary>
    public event Action? MenuPressed;

    /// <summary>A speed was picked: 0 pause toggle, else percent.</summary>
    public event Action<int>? SpeedPicked;

    /// <summary>The button to name a new chief was pressed (shown while the player's tribe needs one).</summary>
    public event Action? ChiefPressed;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "TopBar";
        ThemeTypeVariation = PixelTheme.Variations.Inset;
        MouseFilter = MouseFilterEnum.Stop;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        AddChild(row);
        AddCounter(row, HudArt.Piece(HudArt.PopulationPiece), population, "hud.top.population");
        AddCounter(row, HudArt.Piece(HudArt.FoodPiece), food, "hud.top.food");
        AddCounter(row, HudArt.Piece(HudArt.WoodPiece), wood, "hud.top.wood");
        AddCounter(row, HudArt.Piece(HudArt.StonesPiece), stones, "hud.top.stones");
        AddCounter(row, HudArt.Piece(HudArt.BonesPiece), bones, "hud.top.bones");
        AddCounter(row, HudArt.BarItem(RuleNumbers.Club), weapons, "hud.top.weapons");
        army.StretchMode = TextureRect.StretchModeEnum.KeepCentered;
        army.TooltipText = "hud.top.army";
        row.AddChild(army);
        // While the tribe has no chief the player is asked for one; this stays until they name one.
        chief = new Button
        {
            Name = "ChooseChiefButton",
            Text = "hud.top.choose_chief",
            Icon = HudArt.Painted(PaintedIcon.Chief),
            ThemeTypeVariation = PixelTheme.Variations.Tool,
            FocusMode = FocusModeEnum.None,
            Visible = false,
        };
        chief.Pressed += () => ChiefPressed?.Invoke();
        row.AddChild(chief);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        speedBox = new HBoxContainer { Name = "Speed" };
        speedBox.AddThemeConstantOverride("separation", 1);
        row.AddChild(speedBox);
        menu = SmallButton(PaintedIcon.Menu, "hud.menu", () => MenuPressed?.Invoke(), "MenuButton");
        row.AddChild(menu);
        BuildSpeed();
    }

    /// <summary>Lays the bar out for <paramref name="metrics"/>: the speed and menu buttons only with a mouse.</summary>
    public void Arrange(HudMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        bool mouse = !metrics.Touch;
        speedBox.Visible = mouse && actions.Runner.CanChangeSpeed;
        menu.Visible = mouse;
    }

    /// <summary>Shows the counters of the viewed tribe and the speed state.</summary>
    public void Refresh(int speedPercent, bool paused)
    {
        GameWorld world = actions.World;
        TribeState tribe = world.Tribes[Math.Clamp(actions.ViewTribe, 0, world.Tribes.Count - 1)];
        int weaponCount = 0;
        for (int w = (int)TribeCounter.Clubs; w <= (int)TribeCounter.Tomahawks; w++)
        {
            weaponCount += w == (int)TribeCounter.StoneWeapons ? 0 : Math.Max(0, tribe.Counter(w));
        }
        ArmyOrders call = TribeRoles.Army(world, Math.Clamp(actions.ViewTribe, 0, world.Tribes.Count - 1));
        int armyState = call.AllOut ? 2 : call.Rally ? 1 : 0;
        bool needsChief = !actions.Spectating && actions.Tribe >= 0 && actions.Tribe < world.Tribes.Count
            && TribeRoles.AwaitsNewChief(world, actions.Tribe);
        int hash = HashCode.Combine(HashCode.Combine(tribe[TribeCounter.Population], tribe[TribeCounter.LivingSpace],
            tribe[TribeCounter.Food], tribe[TribeCounter.Wood], tribe[TribeCounter.Stones], tribe[TribeCounter.Bones]),
            weaponCount, armyState, speedPercent, paused, needsChief);
        if (hash == shownHash)
        {
            return;
        }
        shownHash = hash;
        population.Text = $"{tribe[TribeCounter.Population]}/{tribe[TribeCounter.LivingSpace]}";
        food.Text = Number(tribe[TribeCounter.Food]);
        wood.Text = Number(tribe[TribeCounter.Wood]);
        stones.Text = Number(tribe[TribeCounter.Stones]);
        bones.Text = Number(tribe[TribeCounter.Bones]);
        weapons.Text = Number(weaponCount);
        army.Texture = armyState == 2 ? HudArt.Effect(ArmyAllOutEffect) : armyState == 1 ? HudArt.Effect(ArmyRallyEffect) : null;
        army.Visible = armyState != 0;
        chief.Visible = needsChief;
        pause.ButtonPressed = paused;
        int[] percents = { 100, 200, 400 };
        for (int i = 0; i < speeds.Length; i++)
        {
            speeds[i].ButtonPressed = !paused && speedPercent == percents[i];
        }
    }

    private void BuildSpeed()
    {
        pause = SmallButton(PaintedIcon.Pause, "hud.pause", () => SpeedPicked?.Invoke(0), "PauseButton");
        pause.ToggleMode = true;
        speedBox.AddChild(pause);
        PaintedIcon[] icons = { PaintedIcon.Play, PaintedIcon.Fast, PaintedIcon.Fastest };
        int[] percents = { 100, 200, 400 };
        speeds = new Button[icons.Length];
        for (int i = 0; i < icons.Length; i++)
        {
            int percent = percents[i];
            Button button = SmallButton(icons[i], "hud.speed." + percent.ToString(CultureInfo.InvariantCulture), () => SpeedPicked?.Invoke(percent),
                "Speed" + percent.ToString(CultureInfo.InvariantCulture));
            button.ToggleMode = true;
            speeds[i] = button;
            speedBox.AddChild(button);
        }
    }

    private static Button SmallButton(PaintedIcon icon, string tip, Action pressed, string name)
    {
        var button = new Button
        {
            Name = name,
            Icon = HudArt.Painted(icon),
            TooltipText = tip,
            ThemeTypeVariation = PixelTheme.Variations.Tool,
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(20, 16),
            IconAlignment = HorizontalAlignment.Center,
        };
        button.Pressed += pressed;
        return button;
    }

    private static void AddCounter(HBoxContainer row, Texture2D icon, Label value, string tip)
    {
        var box = new HBoxContainer { TooltipText = tip, MouseFilter = MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 2);
        TextureRect picture = HudArt.Icon(icon);
        picture.CustomMinimumSize = new Vector2(Math.Max(12, icon.GetSize().X), 14);
        picture.StretchMode = TextureRect.StretchModeEnum.KeepCentered;
        box.AddChild(picture);
        value.CustomMinimumSize = new Vector2(22, 0);
        box.AddChild(value);
        row.AddChild(box);
    }

    private static string Number(int value) => Math.Max(0, value).ToString(CultureInfo.InvariantCulture);
}
