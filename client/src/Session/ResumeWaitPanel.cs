using System;
using Godot;
using Tribes.Client.Ui;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Net;
using Tribes.Client.Ui.Text;
using Tribes.Net;
using Tribes.Session;

namespace Tribes.Client.Session;

/// <summary>
/// Shown over the match while a resumed hosted match waits for its players: who the save
/// expects (by persistent id), who is back, the address to join, and the host's continue and
/// end buttons. Frees itself once the match runs or stops.
/// </summary>
public partial class ResumeWaitPanel : Control
{
    private readonly NetworkMatchRunner? runner;
    private VBoxContainer list = null!;
    private Label address = null!;
    private Button resume = null!;

    /// <summary>Needed by Godot; shows nothing.</summary>
    public ResumeWaitPanel()
    {
    }

    /// <summary>A panel for <paramref name="runner"/> (only network runners wait for players).</summary>
    public ResumeWaitPanel(IMatchRunner runner)
    {
        this.runner = runner as NetworkMatchRunner;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (runner is null || runner.Phase != MatchPhase.WaitingForPlayers)
        {
            QueueFree();
            return;
        }
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var veil = new PanelContainer { ThemeTypeVariation = PixelTheme.Variations.Veil };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(veil);
        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        VBoxContainer content = UiFactory.Column(6);
        content.CustomMinimumSize = new Vector2(220, 0);
        content.AddChild(UiFactory.Label("session.resume.title", PixelTheme.Variations.Title));
        address = UiFactory.Label("", PixelTheme.Variations.Muted, wrap: true);
        address.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        content.AddChild(address);
        list = UiFactory.Column(2);
        content.AddChild(UiFactory.Panel(PixelTheme.Variations.Inset, list));
        HBoxContainer buttons = UiFactory.Line(6);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        resume = UiFactory.Button("session.resume.continue", Continue, PixelTheme.Variations.Primary);
        buttons.AddChild(resume);
        buttons.AddChild(UiFactory.Button("session.resume.cancel", () => runner.Quit()));
        content.AddChild(buttons);
        centre.AddChild(UiFactory.Panel(PixelTheme.Variations.Sheet, content));

        runner.PlayersChanged += OnChanged;
        runner.PhaseChanged += OnChanged;
        UiText.Changed += Refresh;
        Refresh();
        resume.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (runner is not null)
        {
            runner.PlayersChanged -= OnChanged;
            runner.PhaseChanged -= OnChanged;
        }
        UiText.Changed -= Refresh;
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // An internet room gets its address a moment after the save opened.
        if (runner?.Phase == MatchPhase.WaitingForPlayers && address.Text != UiText.F("session.resume.hint", runner.Host?.Address ?? ""))
        {
            Refresh();
        }
    }

    private void Continue()
    {
        RequestResult result = runner!.ContinueResumed();
        GD.Print($"SESSION continue resumed match: {result}");
    }

    private void Refresh()
    {
        if (runner is null || runner.Phase != MatchPhase.WaitingForPlayers)
        {
            QueueFree();
            return;
        }
        address.Text = UiText.F("session.resume.hint", runner.Host?.Address ?? "");
        foreach (Node child in list.GetChildren())
        {
            child.QueueFree();
        }
        var expected = runner.ExpectedPlayers;
        if (expected.Count == 0)
        {
            list.AddChild(UiFactory.Label("session.resume.none", PixelTheme.Variations.Muted));
        }
        foreach (ExpectedPlayer player in expected)
        {
            HBoxContainer row = UiFactory.Line(4);
            var swatch = new ColorRect
            {
                CustomMinimumSize = new Vector2(8, 8),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                Color = player.Player.Colour >= 0 ? TribeColours.Get(player.Player.Colour).Swatch : Colors.Gray,
            };
            row.AddChild(swatch);
            row.AddChild(UiFactory.Cell(player.Player.Name));
            string state = player.Player.IsFinished
                ? UiText.T("session.resume.spectates")
                : UiText.T(player.IsConnected ? "session.resume.back" : "session.resume.waiting");
            row.AddChild(UiFactory.Plain(state, player.IsConnected ? null : PixelTheme.Variations.Muted));
            list.AddChild(row);
        }
    }
}
