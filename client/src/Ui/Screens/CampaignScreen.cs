using System.Collections.Generic;
using System.Text;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// The campaign: its chapters as the original mission menu lists them (each one is started from
/// its first mission and opens when the one before is won, see <see cref="CampaignChapters"/>),
/// the chosen chapter's briefing with its original story illustration and text, the difficulty
/// and the start button. In portrait the list and the briefing take turns.
/// </summary>
public partial class CampaignScreen : ScreenBase
{
    private readonly List<Button> rows = new();
    private IReadOnlyList<CampaignChapter> chapters = [];
    private CampaignProgress progress = new();
    private BoxContainer split = null!;
    private Control listPane = null!;
    private Control briefingPane = null!;
    private TextureRect illustration = null!;
    private Label missionTitle = null!;
    private Label story = null!;
    private Button easy = null!;
    private Button hard = null!;
    private Button start = null!;
    private CampaignChapter? selected;
    private bool showingBriefing;

    /// <inheritdoc/>
    protected override void Build()
    {
        progress = DevProgress() ?? CampaignProgress.Load();
        chapters = MissionCatalog.Chapters(progress);
        VBoxContainer content = BuildSheet("campaign.title", out _);
        split = new BoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddThemeConstantOverride("separation", 8);
        content.AddChild(split);

        VBoxContainer list = UiFactory.Column(4);
        var group = new ButtonGroup();
        foreach (CampaignChapter chapter in chapters)
        {
            var row = new Button
            {
                Name = "Chapter" + chapter.Number,
                ToggleMode = true,
                ButtonGroup = group,
                Alignment = HorizontalAlignment.Left,
                Disabled = !chapter.Unlocked,
                CustomMinimumSize = new Vector2(0, PixelTheme.RowHeight),
                Icon = chapter.Completed ? PixelTheme.TickIcon : chapter.Unlocked ? null : Icons.Lock,
                IconAlignment = HorizontalAlignment.Right,
                AutoTranslateMode = AutoTranslateModeEnum.Disabled,
                ClipText = true,
            };
            row.Pressed += () => Select(chapter, openBriefing: true);
            list.AddChild(row);
            rows.Add(row);
        }
        listPane = UiFactory.Scroll(list);
        listPane.CustomMinimumSize = new Vector2(190, 0);
        split.AddChild(listPane);

        VBoxContainer briefing = UiFactory.Column(6);
        illustration = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            CustomMinimumSize = new Vector2(240, 130),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        briefing.AddChild(illustration);
        missionTitle = UiFactory.Plain("", PixelTheme.Variations.Heading);
        missionTitle.HorizontalAlignment = HorizontalAlignment.Center;
        briefing.AddChild(missionTitle);
        story = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, AutoTranslateMode = AutoTranslateModeEnum.Disabled };
        PanelContainer storyBox = UiFactory.Panel(PixelTheme.Variations.Inset, UiFactory.Scroll(story));
        storyBox.SizeFlagsVertical = SizeFlags.ExpandFill;
        storyBox.CustomMinimumSize = new Vector2(0, 60);
        briefing.AddChild(storyBox);

        HBoxContainer actions = UiFactory.Line(6);
        actions.AddChild(UiFactory.Label("campaign.difficulty"));
        var difficulty = new ButtonGroup();
        easy = UiFactory.Button("difficulty.easy");
        hard = UiFactory.Button("difficulty.hard");
        foreach (Button choice in new[] { easy, hard })
        {
            choice.ToggleMode = true;
            choice.ButtonGroup = difficulty;
            actions.AddChild(choice);
        }
        (progress.Difficulty == 1 ? hard : easy).ButtonPressed = true;
        actions.AddChild(UiFactory.Spacer(vertical: false));
        start = UiFactory.Button("campaign.start", StartMission, PixelTheme.Variations.Primary);
        start.CustomMinimumSize = new Vector2(80, PixelTheme.RowHeight);
        actions.AddChild(start);
        briefing.AddChild(actions);
        briefingPane = briefing;
        briefing.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        briefing.SizeFlagsVertical = SizeFlags.ExpandFill;
        split.AddChild(briefing);

        RefreshTexts();
        int requested = DevOptions.GetInt("ui-mission", -1);
        CampaignChapter? initial = requested >= 0 && requested < chapters.Count ? chapters[requested] : CampaignChapters.Initial(chapters);
        if (initial is not null)
        {
            Select(initial, openBriefing: requested >= 0);
        }
    }

    /// <summary>"--ui-progress=0,1,2" (for screenshots): the missions won, kept in memory only.</summary>
    private static CampaignProgress? DevProgress()
    {
        string? won = DevOptions.Get("ui-progress");
        if (won is null)
        {
            return null;
        }
        var progress = new CampaignProgress();
        foreach (string part in won.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mission))
            {
                progress.Completed.Add(mission);
            }
        }
        return progress;
    }

    /// <inheritdoc/>
    protected override void RefreshTexts()
    {
        for (int i = 0; i < chapters.Count; i++)
        {
            rows[i].Text = Heading(chapters[i]);
        }
        if (selected is not null)
        {
            ShowBriefing(selected);
        }
    }

    private static string Heading(CampaignChapter chapter) =>
        UiText.F("campaign.number", chapter.Number) + "  " + MissionCatalog.NameOf(chapter);

    private void Select(CampaignChapter chapter, bool openBriefing)
    {
        selected = chapter;
        int index = IndexOf(chapter);
        if (index >= 0)
        {
            rows[index].SetPressedNoSignal(true);
        }
        ShowBriefing(chapter);
        if (openBriefing)
        {
            // Only portrait screens show the list and the briefing one at a time.
            showingBriefing = true;
            Layout(IsPortrait);
        }
    }

    private int IndexOf(CampaignChapter chapter)
    {
        for (int i = 0; i < chapters.Count; i++)
        {
            if (chapters[i].StartMission == chapter.StartMission)
            {
                return i;
            }
        }
        return -1;
    }

    private void ShowBriefing(CampaignChapter chapter)
    {
        // The briefing is the opening of the mission the chapter starts with.
        MissionEntry? mission = MissionCatalog.Find(chapter.StartMission);
        int picture = mission?.Illustration ?? -1;
        string path = $"res://assets/original/pi8_{picture:00}.png";
        illustration.Texture = picture >= 0 && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        illustration.Visible = illustration.Texture is not null;
        missionTitle.Text = Heading(chapter);
        var text = new StringBuilder();
        foreach (int page in mission?.BriefingPages ?? [])
        {
            string line = UiText.Page(page).Trim();
            if (line.Length > 0)
            {
                if (text.Length > 0)
                {
                    text.Append("\n\n");
                }
                text.Append(line);
            }
        }
        if (mission is { IsTutorial: true } && Settings.Tutorial)
        {
            text.Append("\n\n").Append(UiText.T("campaign.tutorial_note"));
        }
        story.Text = text.ToString();
    }

    /// <inheritdoc/>
    protected override void Layout(bool isPortrait)
    {
        split.Vertical = isPortrait;
        listPane.Visible = !isPortrait || !showingBriefing;
        briefingPane.Visible = !isPortrait || showingBriefing;
        listPane.SizeFlagsVertical = SizeFlags.ExpandFill;
        listPane.CustomMinimumSize = new Vector2(isPortrait ? 0 : 190, 0);
    }

    /// <inheritdoc/>
    protected override void GoBack()
    {
        if (IsPortrait && showingBriefing)
        {
            showingBriefing = false;
            Layout(true);
            return;
        }
        base.GoBack();
    }

    private void StartMission()
    {
        if (selected is not { Unlocked: true })
        {
            return;
        }
        int difficulty = hard.ButtonPressed ? 1 : 0;
        // Only the difficulty is kept, read from the file again ("--ui-progress" is never saved).
        CampaignProgress saved = CampaignProgress.Load();
        if (saved.Difficulty != difficulty)
        {
            saved.Difficulty = difficulty;
            saved.Save();
        }
        string name = Settings.PlayerName;
        GameApp.Instance.Match = new MatchRequest
        {
            Mode = MatchMode.Campaign,
            Mission = selected.StartMission,
            Difficulty = difficulty,
            Tutorial = Settings.Tutorial,
            ShowHints = Settings.ShowHints,
            Players =
            [
                new MatchPlayer(0, name, false, -1, 0, 0, true),
                new MatchPlayer(1, UiText.T("player.computer"), true, -1, 1, difficulty, false),
            ],
        };
        MatchLauncher.Launch(Overlay);
    }
}
