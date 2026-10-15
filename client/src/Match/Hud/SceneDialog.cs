using System;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;
using Tribes.Core.Content.Rules;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// A story scene's step as the original shows it: the chapter screen (an illustration on
/// parchment with the page), an animated full-screen picture, or the game with black bars above
/// and below and the speaker's face, name and words. A tap anywhere goes on once the rules take
/// it; the original's "go on" mark shows when they do.
/// </summary>
public partial class SceneDialog : Control
{
    private const int PictureScene = 1, FirstPictureEffect = 98, PictureOverlayEffect = 104;
    private const int PictureWidth = 240, PictureHeight = 128, PictureAnchorX = 120, PictureAnchorY = 64;
    private const double ClockSeconds = 0.12;
    private static readonly int[] ShamanDialogues = { 35, 50, 54 };
    private const int ShamanSlot = 3, LastStoryMissionWithCannibals = 2;

    /// <summary>The original's title of the closing story (s.103 「劇終」); chapter screens otherwise show the chapter's name.</summary>
    private const int ClosingTitle = 103;

    private readonly IMatchActions actions;
    private SceneStep? step;
    private int clock;
    private double clockTime;
    private bool canContinue;
    private PanelContainer textBox = null!;
    private RichTextLabel words = null!;
    private Label speakerName = null!;
    private TextureRect face = null!;
    private TextureRect goOn = null!;
    private TextureRect illustration = null!;
    private PanelContainer chapter = null!;
    private Label chapterTitle = null!;
    private RichTextLabel chapterWords = null!;
    private float slideClosed;
    private bool sliding;
    private TextureRect chapterGoOn = null!;

    /// <summary>A dialog for the match <paramref name="actions"/> plays.</summary>
    public SceneDialog(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The player tapped to go on.</summary>
    public event Action? ContinueRequested;

    /// <summary>The step shown, or null.</summary>
    public SceneStep? Step => step;

    /// <summary>
    /// The first step sliding in (<see cref="Logic.SceneSlide"/>): <paramref name="closed"/> how far
    /// the black bars cover the game (0..1); the words show once the slide is over.
    /// </summary>
    public void ShowSlide(bool slidingIn, float closed)
    {
        if (slidingIn == sliding && Mathf.IsEqualApprox(closed, slideClosed))
        {
            return;
        }
        sliding = slidingIn;
        slideClosed = closed;
        if (step is { ChapterScreen: false })
        {
            textBox.Visible = !sliding && GameTexts.Clean(UiText.Page(step.Page)).Length > 0;
        }
        QueueRedraw();
    }

    /// <summary>The page text shown (for checks).</summary>
    public string ShownText => step is null ? "" : words.Visible ? words.GetParsedText() : chapterWords.GetParsedText();

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "SceneDialog";
        MouseFilter = MouseFilterEnum.Stop;
        // The interface gives it the whole screen at every layout (MatchHud.Relayout), so it keeps plain anchors.
        BuildTextBox();
        BuildChapter();
        Visible = false;
    }

    /// <summary>Shows <paramref name="shown"/> (null hides the dialog); <paramref name="continues"/> whether a tap is taken now.</summary>
    public void Show(SceneStep? shown, bool continues)
    {
        canContinue = continues;
        if (shown == step)
        {
            goOn.Visible = chapterGoOn.Visible = continues;
            return;
        }
        step = shown;
        Visible = shown is not null;
        if (shown is null)
        {
            return;
        }
        GameWorld world = actions.World;
        StoryScene? scene = shown.Scene >= 0 && shown.Scene < world.Content.Rules.Story.Scenes.Count ? world.Content.Rules.Story.Scenes[shown.Scene] : null;
        string text = UiText.Page(shown.Page);
        bool hasText = GameTexts.Clean(text).Length > 0;
        chapter.Visible = shown.ChapterScreen;
        textBox.Visible = !shown.ChapterScreen && hasText && !sliding;
        if (shown.ChapterScreen)
        {
            string path = $"res://assets/original/pi8_{shown.Illustration:00}.png";
            illustration.Texture = shown.Illustration >= 0 && ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            illustration.Visible = illustration.Texture is not null;
            // The original's title panel of the chapter screen: the chapter's name, or 「劇終」 at the end.
            chapterTitle.Text = shown.Dialogue == Tribes.Core.Simulation.GameSimulation.ClosingDialogue
                ? UiText.S(ClosingTitle)
                : ObjectivePanel.ChapterName(actions, world.Mission);
            GameTexts.Fill(chapterWords, text, actions.Metrics.IconScale);
        }
        else
        {
            GameTexts.Fill(words, text, actions.Metrics.IconScale);
            ShowSpeaker(world, shown);
        }
        goOn.Visible = chapterGoOn.Visible = continues;
        IsPicture = scene is { Mode: PictureScene };
        Picture = scene?.PoseOrPicture ?? 0;
        QueueRedraw();
        Arrange();
    }

    /// <summary>Whether the step is an animated full-screen picture.</summary>
    public bool IsPicture { get; private set; }

    /// <summary>The picture of a picture step.</summary>
    public int Picture { get; private set; }

    /// <summary>Lays the parts out for the current size.</summary>
    public void Arrange()
    {
        float width = Mathf.Min(Size.X - 8, 520);
        textBox.Size = new Vector2(width, 0);
        float height = textBox.GetCombinedMinimumSize().Y;
        textBox.Position = new Vector2((Size.X - width) / 2, Size.Y - height - Mathf.Max(4, Size.Y * 0.04f)).Floor();
        float chapterWidth = Mathf.Min(Size.X - 8, 560);
        chapter.Size = new Vector2(chapterWidth, Mathf.Min(Size.Y - 8, Mathf.Max(200, Size.Y * 0.9f)));
        chapter.Position = ((Size - chapter.Size) / 2).Floor();
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        if (what == NotificationResized && textBox is not null && chapter is not null)
        {
            Arrange();
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }
        clockTime += delta;
        if (clockTime >= ClockSeconds)
        {
            clockTime = 0;
            clock++;
            if (IsPicture || step is { ChapterScreen: false, Staged: true })
            {
                QueueRedraw();
            }
            if (canContinue)
            {
                bool blink = (clock / 4) % 2 == 0;
                goOn.Modulate = chapterGoOn.Modulate = new Color(1, 1, 1, blink ? 1 : 0.4f);
            }
        }
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (step is null || step.ChapterScreen)
        {
            return;
        }
        if (IsPicture)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black);
            int scale = Math.Max(1, (int)Mathf.Min(Size.X / PictureWidth, (Size.Y * 0.62f) / PictureHeight));
            var origin = new Vector2((Size.X - (PictureWidth * scale)) / 2, (Size.Y * 0.36f) - (PictureHeight * scale / 2f)).Floor();
            DrawSetTransform(origin + new Vector2(PictureAnchorX * scale, PictureAnchorY * scale), 0, new Vector2(scale, scale));
            OriginalArt.Shared.DrawEffect(this, FirstPictureEffect + Picture, clock, Vector2.Zero);
            OriginalArt.Shared.DrawEffect(this, PictureOverlayEffect, clock, Vector2.Zero);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }
        // The original's black bars above and below the staged scene; while the first step slides
        // in they close over the game and open again.
        float bar = Mathf.Floor(Mathf.Lerp(Size.Y * 0.09f, Size.Y / 2 + 1, sliding ? slideClosed : 0));
        DrawRect(new Rect2(0, 0, Size.X, bar), Colors.Black);
        DrawRect(new Rect2(0, Size.Y - bar, Size.X, bar), Colors.Black);
    }

    /// <inheritdoc/>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } or InputEventScreenTouch { Pressed: true })
        {
            AcceptEvent();
            ContinueRequested?.Invoke();
        }
    }

    private void ShowSpeaker(GameWorld world, SceneStep shown)
    {
        int unitId = shown.Speaker != 0 ? shown.FocusUnit : 0;
        bool known = unitId >= 1 && unitId <= world.UnitSlots;
        face.Visible = speakerName.Visible = known;
        if (!known)
        {
            return;
        }
        Unit unit = world.Unit(unitId);
        // In the first missions every enemy speaks as the cannibals' leader (their first unit).
        int leader = world.FirstUnitSlot(unit.Tribe);
        if (world.Mission <= LastStoryMissionWithCannibals && unit.Tribe != Math.Max(0, actions.Tribe) && leader >= 1 && leader <= world.UnitSlots)
        {
            unit = world.Unit(leader);
        }
        bool shaman = shown.Speaker == ShamanSlot && Array.IndexOf(ShamanDialogues, shown.Dialogue) >= 0;
        speakerName.Text = GameTexts.SpeakerName(world, unit, shaman);
        face.Texture = HudWidgets.Enlarge(HudArt.Face(unit.Portrait, actions.ColourOf(unit.Tribe)), actions.Metrics.Touch ? 2 : 1);
    }

    private void BuildTextBox()
    {
        textBox = new PanelContainer { Name = "SceneText", ThemeTypeVariation = PixelTheme.Variations.Sheet, MouseFilter = MouseFilterEnum.Ignore };
        HBoxContainer row = HudPanels.Row(6);
        face = new TextureRect { StretchMode = TextureRect.StretchModeEnum.KeepCentered, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkBegin };
        row.AddChild(HudPanels.Framed(face));
        VBoxContainer text = HudPanels.Column(2);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        speakerName = HudWidgets.Value("", PixelTheme.Variations.Heading);
        text.AddChild(speakerName);
        words = NewWords("SceneWords");
        text.AddChild(words);
        row.AddChild(text);
        goOn = HudArt.Icon(HudArt.Sprite(HudArt.ContinueSprite), 2);
        goOn.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        row.AddChild(goOn);
        textBox.AddChild(row);
        AddChild(textBox);
    }

    private void BuildChapter()
    {
        chapter = new PanelContainer { Name = "Chapter", ThemeTypeVariation = PixelTheme.Variations.Sheet, MouseFilter = MouseFilterEnum.Ignore };
        VBoxContainer column = HudPanels.Column(6);
        chapterTitle = HudWidgets.Value("", PixelTheme.Variations.Title);
        chapterTitle.Name = "ChapterTitle";
        chapterTitle.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(chapterTitle);
        illustration = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 100),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.AddChild(illustration);
        chapterWords = NewWords("ChapterWords");
        column.AddChild(chapterWords);
        chapterGoOn = HudArt.Icon(HudArt.Sprite(HudArt.ContinueSprite), 2);
        chapterGoOn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        column.AddChild(chapterGoOn);
        chapter.AddChild(column);
        AddChild(chapter);
    }

    private static RichTextLabel NewWords(string name) => new()
    {
        Name = name,
        FitContent = true,
        ScrollActive = false,
        BbcodeEnabled = false,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        MouseFilter = MouseFilterEnum.Ignore,
        CustomMinimumSize = new Vector2(120, 0),
    };
}
