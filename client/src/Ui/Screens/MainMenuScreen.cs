using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Platform;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;

namespace Tribes.Client.Ui.Screens;

/// <summary>
/// The main menu: the original title screen (logo, camp scenery) with the menu on a parchment
/// sheet. Landscape puts the scenery on the left and the menu on the right; portrait stacks
/// logo, menu and scenery.
/// </summary>
public partial class MainMenuScreen : ScreenBase
{
    private const int LogoSprite = 1665;

    private readonly List<MenuEntry> entries = new();
    private TextureRect logo = null!;
    private PanelContainer menu = null!;
    private Label version = null!;
    private static bool appUpdateShown;

    /// <inheritdoc/>
    protected override string? BackScreen => null;

    /// <inheritdoc/>
    protected override void Build()
    {
        logo = new TextureRect
        {
            Texture = OriginalArt.Shared.SpriteTexture(LogoSprite),
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(logo);

        menu = new PanelContainer { ThemeTypeVariation = PixelTheme.Variations.Sheet };
        AddChild(menu);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        menu.AddChild(column);
        AddEntry(column, "menu.campaign", () => UiScreens.Go(UiScreens.Campaign));
        AddEntry(column, "menu.custom", () => UiScreens.Go(UiScreens.CustomGame));
        AddEntry(column, "menu.multiplayer", () => UiScreens.Go(UiScreens.Multiplayer));
        AddEntry(column, "menu.endless", () => UiScreens.Go(UiScreens.Endless));
        AddEntry(column, "menu.load", () => UiScreens.Go(UiScreens.LoadGame));
        AddEntry(column, "menu.settings", () => UiScreens.Go(UiScreens.Settings));
        AddEntry(column, "menu.update", () => UiScreens.Go(UiScreens.Update));
        AddEntry(column, "menu.credits", () => UiScreens.Go(UiScreens.Credits));
        if (OS.GetName() != "Android")
        {
            AddEntry(column, "menu.quit", () => GetTree().Quit());
        }
        Resized += PlaceAll;

        version = UiFactory.Plain("v" + BuildInfo.Current.Version, PixelTheme.Variations.OnDark);
        AddChild(version);
        version.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        version.GrowHorizontal = GrowDirection.Begin;
        version.GrowVertical = GrowDirection.Begin;
        version.OffsetRight = -6;
        version.OffsetBottom = -3;

        entries[0].CallDeferred(Control.MethodName.GrabFocus);
        CallDeferred(MethodName.ShowAppUpdateResult);
    }

    private void AddEntry(VBoxContainer column, string key, Action action)
    {
        var entry = new MenuEntry(key);
        entry.Pressed += action;
        column.AddChild(entry);
        entries.Add(entry);
    }

    /// <inheritdoc/>
    protected override void RefreshTexts() => CallDeferred(MethodName.PlaceAll);

    /// <inheritdoc/>
    protected override void Layout(bool isPortrait)
    {
        PlaceAll();
    }

    /// <summary>
    /// Places the logo and the menu in the sky above the scenery, which stands on the bottom edge:
    /// side by side in landscape (scenery left, menu right), stacked in portrait. When the sky is
    /// too low for the menu, its foot reaches over the forest beside the camp, never over the camp.
    /// </summary>
    private void PlaceAll()
    {
        bool isPortrait = Size.Y > Size.X;
        Backdrop.SceneCenter = isPortrait ? 0.5f : 0.3f;
        Backdrop.BottomCrop = isPortrait ? 0 : 4;
        Rect2 scene = Backdrop.SceneRect;
        Rect2 sky = Backdrop.SkyRect;
        Vector2 menuSize = menu.GetCombinedMinimumSize();
        menuSize.X = Math.Max(menuSize.X, 200);
        menu.Size = menuSize;
        if (isPortrait)
        {
            logo.Size = logo.Texture.GetSize();
            const float gap = 8;
            float group = logo.Size.Y + gap + menuSize.Y;
            float top = MathF.Floor(Math.Max(6, (sky.Size.Y - group) * 0.45f));
            logo.Position = new Vector2(MathF.Floor((Size.X - logo.Size.X) / 2), top);
            menu.Position = new Vector2(MathF.Floor((Size.X - menuSize.X) / 2), top + logo.Size.Y + gap);
            return;
        }
        float right = Size.X - 16 - menuSize.X;
        float x = Math.Min(right, Math.Max(scene.End.X + 8, right - Math.Max(0, (Size.X - 640) / 4)));
        // In the sky when it is tall enough; otherwise from the top, its foot over the forest to the right of the camp.
        float y = menuSize.Y <= sky.Size.Y - 8 ? (sky.Size.Y - menuSize.Y) / 2 : 4;
        menu.Position = new Vector2(MathF.Floor(x), MathF.Floor(Math.Max(4, y)));
        float sceneMiddle = scene.Position.X + scene.Size.X / 2;
        Vector2 logoSize = logo.Texture.GetSize();
        bool large = sky.Size.Y >= logoSize.Y * 2 + 140 && sceneMiddle + logoSize.X <= menu.Position.X - 8;
        logo.Size = logoSize * (large ? 2 : 1);
        logo.Position = new Vector2(MathF.Floor(Math.Max(4, sceneMiddle - logo.Size.X / 2)), MathF.Floor(Math.Max(6, (sky.Size.Y - logo.Size.Y) / 2)));
    }

    /// <summary>Tells the player how the last Windows app update ended (once per run).</summary>
    private void ShowAppUpdateResult()
    {
        if (appUpdateShown || GameApp.Instance.LastAppUpdate is not { } result)
        {
            return;
        }
        appUpdateShown = true;
        string key = result.Status switch
        {
            "succeeded" => "update.swap.succeeded",
            "rolledBack" => "update.swap.rolled_back",
            "rollbackFailed" => "update.swap.rollback_failed",
            "notRun" => "update.swap.not_run",
            _ => "update.swap.failed",
        };
        ShowNote(UiText.F(key, result.Version));
    }

    /// <inheritdoc/>
    protected override void GoBack()
    {
        ModalDialog.Show(Overlay, "menu.quit_confirm", (UiKeys.Yes, () => GetTree().Quit()), (UiKeys.No, null));
    }
}
