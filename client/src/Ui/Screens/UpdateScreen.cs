using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Platform;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Text;
using Tribes.Update;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// Check for updates (docs/UPDATES.md): check → version and notes → download with progress →
/// apply. A content pack is used after a restart, a Windows update closes the game to swap the
/// files, an Android update opens the system installer. Errors show
/// <see cref="UpdateException.UserMessage"/>.
/// </summary>
public partial class UpdateScreen : ScreenBase
{
    private const string IconPath = "res://assets/original/icon.png";

    private Label current = null!;
    private Label status = null!;
    private Label notes = null!;
    private Control notesBox = null!;
    private ProgressBar progress = null!;
    private Button check = null!;
    private Button download = null!;
    private Button cancel = null!;
    private Button restart = null!;
    private CancellationTokenSource? work;
    private UpdateCheckResult? found;

    /// <inheritdoc/>
    protected override void Build()
    {
        VBoxContainer content = BuildSheet("update.title", out _);
        content.AddChild(Gap());
        content.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>(IconPath),
            StretchMode = TextureRect.StretchModeEnum.KeepCentered,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        });
        current = UiFactory.Plain("", PixelTheme.Variations.Muted);
        current.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(current);
        status = UiFactory.Label("", PixelTheme.Variations.Heading, wrap: true);
        status.AutoTranslateMode = AutoTranslateModeEnum.Disabled;
        status.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(status);
        notes = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, AutoTranslateMode = AutoTranslateModeEnum.Disabled };
        PanelContainer box = UiFactory.Panel(PixelTheme.Variations.Inset, UiFactory.Scroll(notes));
        box.SizeFlagsVertical = SizeFlags.ExpandFill;
        box.SizeFlagsStretchRatio = 6;
        notesBox = box;
        content.AddChild(box);
        progress = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = true, CustomMinimumSize = new Vector2(0, 16) };
        content.AddChild(progress);
        HBoxContainer buttons = UiFactory.Line(8);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        check = UiFactory.Button("update.check", () => _ = CheckAsync(), PixelTheme.Variations.Primary);
        download = UiFactory.Button("update.download", () => _ = DownloadAsync(), PixelTheme.Variations.Primary);
        cancel = UiFactory.Button("update.cancel", () => work?.Cancel());
        restart = UiFactory.Button("update.restart", Restart, PixelTheme.Variations.Primary);
        foreach (Button button in new[] { check, download, cancel, restart })
        {
            button.CustomMinimumSize = new Vector2(110, PixelTheme.RowHeight);
            buttons.AddChild(button);
        }
        content.AddChild(buttons);
        content.AddChild(Gap());
        RefreshTexts();
        ShowIdle();
        if (DevOptions.Has("update-check"))
        {
            IsSettled = false;
            CallDeferred(MethodName.StartCheckFromDev);
        }
        else
        {
            check.CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    private void StartCheckFromDev() => _ = CheckAsync();

    /// <summary>Space above and below that centres the screen while the notes are hidden.</summary>
    private static Control Gap() => new() { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        work?.Cancel();
        base._ExitTree();
    }

    /// <inheritdoc/>
    protected override void RefreshTexts()
    {
        current.Text = UiText.F("update.current", BuildInfo.Current.Version, GameApp.Instance.ContentRevision);
    }

    /// <summary>Whether the screen is still shown (an awaited step may finish after the player left).</summary>
    private bool Alive => IsInstanceValid(this) && IsInsideTree();

    private void ShowState(string text, bool showNotes, bool showProgress, params Button[] visible)
    {
        if (!Alive)
        {
            return;
        }
        status.Text = text;
        notesBox.Visible = showNotes;
        progress.Visible = showProgress;
        foreach (Button button in new[] { check, download, cancel, restart })
        {
            button.Visible = Array.IndexOf(visible, button) >= 0;
        }
        if (visible.Length > 0)
        {
            visible[0].CallDeferred(Control.MethodName.GrabFocus);
        }
    }

    private void ShowIdle()
    {
        if (GameApp.Instance.Updates is null)
        {
            ShowState(GameApp.Instance.UpdatesUnavailable?.UserMessage ?? UiText.T("update.unavailable"), false, false);
            IsSettled = true;
            return;
        }
        ShowState(UiText.T("update.idle"), false, false, check);
    }

    private async Task CheckAsync()
    {
        UpdateManager? updates = GameApp.Instance.Updates;
        if (updates is null)
        {
            ShowIdle();
            return;
        }
        IsSettled = false;
        work = new CancellationTokenSource();
        ShowState(UiText.T("update.checking"), false, false, cancel);
        try
        {
            UpdateCheckResult result = await updates.CheckAsync(work.Token);
            if (!Alive)
            {
                return;
            }
            GD.Print($"UPDATE check kind={result.Kind} tag={result.Tag} needsApp={result.NewerContentNeedsNewerApp}");
            if (result.Kind == UpdateKind.None)
            {
                ShowState(UiText.T(result.NewerContentNeedsNewerApp ? "update.needs_app" : "update.up_to_date"), false, false, check);
            }
            else
            {
                found = result;
                string kind = UiText.T(result.Kind == UpdateKind.App ? "update.kind_app" : "update.kind_content");
                long size = result.Asset?.Size ?? 0;
                ShowState(UiText.F("update.available", result.Tag?.ToString() ?? "?", kind, Megabytes(size)), true, false, download, check);
                notes.Text = result.Notes.Length > 0 ? result.Notes : UiText.T("update.no_notes");
            }
        }
        catch (OperationCanceledException)
        {
            ShowIdle();
        }
        catch (UpdateException e)
        {
            GD.Print($"UPDATE check failed: {e.Kind} {e.Message}");
            ShowState(e.UserMessage, false, false, check);
        }
        finally
        {
            if (Alive)
            {
                IsSettled = true;
            }
        }
    }

    private async Task DownloadAsync()
    {
        UpdateManager? updates = GameApp.Instance.Updates;
        if (updates is null || found is null)
        {
            return;
        }
        work = new CancellationTokenSource();
        progress.Value = 0;
        ShowState(UiText.T("update.downloading"), true, true, cancel);
        try
        {
            var report = new Progress<DownloadProgress>(p =>
            {
                if (!Alive)
                {
                    return;
                }
                progress.Value = p.Fraction;
                status.Text = UiText.F("update.downloading_bytes", Megabytes(p.BytesReceived), Megabytes(p.TotalBytes));
            });
            string file = await updates.DownloadAsync(found, report, work.Token);
            ShowState(UiText.T("update.applying"), true, false);
            UpdateApplyOutcome outcome = await updates.ApplyAsync(found, file, work.Token);
            GD.Print($"UPDATE applied {outcome}");
            switch (outcome)
            {
                case UpdateApplyOutcome.RestartToApply:
                    ShowState(UiText.T("update.restart_needed"), false, false, restart);
                    break;
                case UpdateApplyOutcome.QuitToFinish:
                    ShowState(UiText.T("update.closing"), false, false);
                    SceneTree tree = GetTree();
                    tree.CreateTimer(1.5).Timeout += () => tree.Quit();
                    break;
                case UpdateApplyOutcome.PermissionRequested:
                    ShowState(UiText.T("update.permission"), false, false, download);
                    break;
                case UpdateApplyOutcome.InstallerStarted:
                    ShowState(UiText.T("update.installer"), false, false, check);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            ShowState(UiText.T("update.cancelled"), true, false, download, check);
        }
        catch (UpdateException e)
        {
            GD.Print($"UPDATE failed: {e.Kind} {e.Message}");
            ShowState(e.UserMessage, true, false, download, check);
        }
    }

    private void Restart()
    {
        OS.SetRestartOnExit(true);
        GetTree().Quit();
    }

    private static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);
}
