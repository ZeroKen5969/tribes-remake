using System.Collections.Generic;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Net;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// Custom game against one computer tribe: one of the custom maps (with its minimap), the
/// difficulty and the player's tribe colour.
/// </summary>
public partial class CustomGameScreen : ScreenBase
{
    private readonly List<Button> mapButtons = new();
    private IReadOnlyList<MissionEntry> maps = [];
    private BoxContainer split = null!;
    private MapPreview preview = null!;
    private Label mapInfo = null!;
    private ColourPicker colours = null!;
    private Button hard = null!;
    private MissionEntry? selected;
    private int colour;

    /// <inheritdoc/>
    protected override void Build()
    {
        maps = MissionCatalog.CustomMaps;
        VBoxContainer content = BuildSheet("custom.title", out _);
        split = new BoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 10);
        content.AddChild(split);

        VBoxContainer left = UiFactory.Column(4);
        left.AddChild(UiFactory.Label("custom.map", PixelTheme.Variations.Heading));
        var group = new ButtonGroup();
        foreach (MissionEntry map in maps)
        {
            Button button = UiFactory.Button("");
            button.Name = "Map" + map.Mission;
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.Alignment = HorizontalAlignment.Left;
            button.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
            button.Pressed += () => Select(map);
            left.AddChild(button);
            mapButtons.Add(button);
        }
        preview = new MapPreview { CustomMinimumSize = new Vector2(120, 100), SizeFlagsVertical = SizeFlags.ExpandFill };
        left.AddChild(preview);
        mapInfo = UiFactory.Plain("", PixelTheme.Variations.Muted);
        mapInfo.HorizontalAlignment = HorizontalAlignment.Center;
        left.AddChild(mapInfo);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        split.AddChild(left);

        VBoxContainer right = UiFactory.Column(8);
        right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        right.AddChild(UiFactory.Label("custom.difficulty", PixelTheme.Variations.Heading));
        HBoxContainer difficulty = UiFactory.Line(6);
        var difficultyGroup = new ButtonGroup();
        Button easy = UiFactory.Button("difficulty.easy");
        hard = UiFactory.Button("difficulty.hard");
        foreach (Button choice in new[] { easy, hard })
        {
            choice.ToggleMode = true;
            choice.ButtonGroup = difficultyGroup;
            choice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            difficulty.AddChild(choice);
        }
        easy.ButtonPressed = true;
        right.AddChild(difficulty);
        right.AddChild(UiFactory.Label("custom.colour", PixelTheme.Variations.Heading));
        colours = new ColourPicker(PickColour);
        right.AddChild(colours);
        right.AddChild(UiFactory.Label("custom.opponent", PixelTheme.Variations.Muted, wrap: true));
        right.AddChild(UiFactory.Spacer());
        Button start = UiFactory.Button("custom.start", StartGame, PixelTheme.Variations.Primary);
        right.AddChild(start);
        split.AddChild(right);

        PickColour(0);
        RefreshTexts();
        if (maps.Count > 0)
        {
            Select(maps[0]);
            mapButtons[0].SetPressedNoSignal(true);
            mapButtons[0].CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    /// <inheritdoc/>
    protected override void RefreshTexts()
    {
        for (int i = 0; i < maps.Count; i++)
        {
            mapButtons[i].Text = maps[i].Name;
        }
        if (selected is not null)
        {
            mapInfo.Text = MapInfo(selected);
        }
    }

    /// <summary>The map's size and how many tribes it has room for.</summary>
    private static string MapInfo(MissionEntry map) => MissionCatalog.KnownTribeLimit(map.Mission) is int limit
        ? UiText.F("custom.map_room", map.Width, map.Height, limit)
        : UiText.F("custom.map_size", map.Width, map.Height);

    /// <inheritdoc/>
    protected override void Layout(bool isPortrait) => split.Vertical = isPortrait;

    private void Select(MissionEntry map)
    {
        selected = map;
        preview.ShowMission(map.Mission);
        mapInfo.Text = MapInfo(map);
        MissionCatalog.WhenCapacitiesKnown(() =>
        {
            if (IsInsideTree() && selected == map)
            {
                mapInfo.Text = MapInfo(map);
            }
        });
    }

    private void PickColour(int id)
    {
        colour = id;
        colours.SetState(id, new HashSet<int>());
    }

    private void StartGame()
    {
        if (selected is null)
        {
            return;
        }
        // The player and one computer tribe (every custom map has room for them; checked anyway).
        if (MissionCatalog.Check(selected.Mission, 1, 2) is var problem and not Logic.MatchProblem.None)
        {
            ShowMessage(MissionCatalog.ProblemText(problem, selected.Mission, 2));
            return;
        }
        int difficulty = hard.ButtonPressed ? 1 : 0;
        int computerColour = colour == 1 ? 0 : 1;
        GameApp.Instance.Match = new MatchRequest
        {
            Mode = MatchMode.Custom,
            Mission = selected.Mission,
            Difficulty = difficulty,
            ShowHints = Settings.ShowHints,
            Players =
            [
                new MatchPlayer(0, Settings.PlayerName, false, colour, 0, 0, true),
                new MatchPlayer(1, UiText.T("player.computer"), true, computerColour, 1, difficulty, false),
            ],
        };
        MatchLauncher.Launch(Overlay);
    }
}
