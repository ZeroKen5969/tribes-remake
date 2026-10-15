using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Net;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// Saved games of every mode (user://saves/, see <see cref="SaveCatalog"/>), newest first: each
/// with its picture (or a stand-in), mode and mission, slot, date and play time; load or delete.
/// The pictures are shown full size when the rows are wide enough, else at half size with the
/// buttons stacked.
/// </summary>
public partial class LoadGameScreen : ScreenBase
{
    private readonly List<(SaveThumbnail Picture, BoxContainer Buttons)> rows = new();
    private VBoxContainer list = null!;
    private ScrollContainer scroll = null!;

    /// <summary>"--ui-saves=FOLDER" (for screenshots): list the saves of another folder instead.</summary>
    private static string? DevFolder => DevOptions.Get("ui-saves") is { Length: > 0 } folder ? folder : null;

    /// <inheritdoc/>
    protected override void Build()
    {
        VBoxContainer content = BuildSheet("load.title", out _);
        list = UiFactory.Column(4);
        scroll = UiFactory.Scroll(list);
        PanelContainer box = UiFactory.Panel(PixelTheme.Variations.Inset, scroll);
        box.SizeFlagsVertical = SizeFlags.ExpandFill;
        content.AddChild(box);
        scroll.Resized += FitRows;
        ShowSaves();
    }

    /// <inheritdoc/>
    protected override void RefreshTexts() => ShowSaves();

    private void ShowSaves()
    {
        foreach (Node child in list.GetChildren())
        {
            child.QueueFree();
        }
        rows.Clear();
        IReadOnlyList<SaveInfo> saves = DevFolder is { } folder ? SaveCatalog.ListIn(folder) : SaveCatalog.List();
        if (saves.Count == 0)
        {
            Label none = UiFactory.Label("load.none", PixelTheme.Variations.Muted, wrap: true);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            list.AddChild(UiFactory.Spacer());
            list.AddChild(none);
            return;
        }
        bool focused = false;
        foreach (SaveInfo save in saves)
        {
            Button load = AddRow(save);
            if (!focused)
            {
                load.CallDeferred(Control.MethodName.GrabFocus);
                focused = true;
            }
        }
        FitRows();
    }

    /// <summary>Adds the row of <paramref name="save"/> and returns its load button.</summary>
    private Button AddRow(SaveInfo save)
    {
        HBoxContainer row = UiFactory.Line(6);
        var picture = new SaveThumbnail(SaveCatalog.ThumbnailPath(save));
        row.AddChild(picture);

        VBoxContainer text = UiFactory.Column(0);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        text.AddChild(UiFactory.Cell(UiText.F("load.heading", ModeName(save.Mode), Title(save))));
        string slot = save.Slot == "auto" ? UiText.T("load.autosave") : UiText.F("load.slot", save.Slot);
        text.AddChild(UiFactory.Cell(slot, PixelTheme.Variations.Muted));
        string when = save.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        text.AddChild(UiFactory.Cell(when, PixelTheme.Variations.Muted));
        text.AddChild(UiFactory.Cell(UiText.F("load.played", SaveCatalog.PlayTime(save.PlaySeconds)), PixelTheme.Variations.Muted));
        if (SaveSettingsText.Line(save) is { } settings)
        {
            text.AddChild(settings);
        }
        row.AddChild(text);

        var buttons = new BoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 4);
        Button load = UiFactory.Button("load.load", () => Load(save));
        load.Name = "Load" + rows.Count;
        load.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        buttons.AddChild(load);
        var delete = new Button
        {
            Name = "Delete" + rows.Count,
            Icon = PixelTheme.CrossIcon,
            IconAlignment = HorizontalAlignment.Center,
            ThemeTypeVariation = PixelTheme.Variations.Tool,
            TooltipText = "load.delete",
            CustomMinimumSize = new Vector2(PixelTheme.RowHeight, PixelTheme.RowHeight),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = SizeFlags.ShrinkEnd,
        };
        delete.Pressed += () => ModalDialog.Show(Overlay, "load.delete_confirm", (UiKeys.Yes, () =>
        {
            if (!DeleteBeside(save))
            {
                ShowNote(UiText.T("load.delete_failed"));
            }
            ShowSaves();
        }), (UiKeys.No, null));
        buttons.AddChild(delete);
        row.AddChild(buttons);

        list.AddChild(UiFactory.Panel(PixelTheme.Variations.Slot, row));
        rows.Add((picture, buttons));
        return load;
    }

    /// <summary>Sizes the pictures for the current row width; narrow rows stack the buttons.</summary>
    private void FitRows()
    {
        float width = scroll.Size.X;
        (int Width, int Height) size = SaveThumbnails.DisplaySize(width);
        bool full = size.Width == SaveThumbnails.Width;
        foreach ((SaveThumbnail picture, BoxContainer buttons) in rows)
        {
            picture.ShowAt(size);
            buttons.Vertical = !full;
        }
    }

    /// <summary>
    /// Deletes the save the row was listed from: its data, description and picture, named after
    /// the data file found in the folder (not after the description's mode and slot). False when a
    /// file could not be removed.
    /// </summary>
    private static bool DeleteBeside(SaveInfo save)
    {
        if (!save.DataPath.EndsWith(".save", StringComparison.Ordinal))
        {
            SaveCatalog.Delete(save);
            return true;
        }
        string stem = save.DataPath[..^".save".Length];
        bool deleted = true;
        foreach (string path in new[] { save.DataPath, stem + ".json", stem + ".png" })
        {
            if (FileAccess.FileExists(path) && DirAccess.RemoveAbsolute(path) is Error error and not Error.Ok)
            {
                GD.PushWarning($"{path} not deleted: {error}");
                deleted = false;
            }
        }
        return deleted;
    }

    private static string ModeName(string mode) => UiText.T("mode." + mode);

    private static string Title(SaveInfo save)
    {
        if (save.Title.Length > 0)
        {
            return save.Title;
        }
        return MissionCatalog.Find(save.Mission)?.Name ?? UiText.T("endless.world");
    }

    /// <summary>
    /// Loads <paramref name="save"/>. A hosted match's save asks first where its room opens again:
    /// on the local network or on the internet (the players come back the same way).
    /// </summary>
    private void Load(SaveInfo save)
    {
        if (InternetPlay.IsAvailable && Tribes.Client.Session.MatchSessions.ReadHeader(save.DataPath)?.Kind == Tribes.Session.SaveKind.HostedMatch)
        {
            ModalDialog.Show(Overlay, "load.resume_where",
                ("load.resume_lan", () => Start(save, onInternet: false)),
                ("load.resume_internet", () => Start(save, onInternet: true)),
                (UiKeys.Cancel, null));
            return;
        }
        Start(save, onInternet: false);
    }

    private void Start(SaveInfo save, bool onInternet)
    {
        MatchMode mode = Enum.TryParse(save.Mode, ignoreCase: true, out MatchMode parsed) ? parsed : MatchMode.Campaign;
        GameApp.Instance.Match = new MatchRequest
        {
            Mode = mode,
            Mission = save.Mission,
            Difficulty = save.Difficulty,
            SaveToLoad = save.DataPath,
            ResumeOnInternet = onInternet,
            Tutorial = Settings.Tutorial,
            ShowHints = Settings.ShowHints,
        };
        MatchLauncher.Launch(Overlay);
    }
}
