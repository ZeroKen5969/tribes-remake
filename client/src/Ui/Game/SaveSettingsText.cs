using System.Collections.Generic;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Logic;
using Tribes.Client.Ui.Text;
using Tribes.Session;

namespace Tribes.Client.Ui.Game;

/// <summary>The line about a save's world settings: the computers' difficulty, the opening grace, the start stock, the wild animals.</summary>
public static class SaveSettingsText
{
    /// <summary>The parts of the line for <paramref name="save"/>, or null when it has no settings (the campaign, older saves).</summary>
    public static IReadOnlyList<string>? Parts(SaveInfo save)
    {
        System.ArgumentNullException.ThrowIfNull(save);
        if (save.Settings is not { } world)
        {
            return null;
        }
        SaveSettingsParts parts = SaveSettingsLine.For(save.Difficulty, world.OpeningGraceFrames, world.StartStockPercent, world.WildAnimals, MatchPlan.FramesPerMinute);
        return
        [
            UiText.F("load.settings.difficulty", UiText.T(parts.DifficultyKey)),
            parts.GraceMinutes > 0 ? UiText.F("load.settings.grace", parts.GraceMinutes) : UiText.T("load.settings.no_grace"),
            UiText.F("load.settings.stock", parts.Stock),
            UiText.T(parts.WildAnimals ? "load.settings.animals" : "load.settings.no_animals"),
        ];
    }

    /// <summary>
    /// The line as muted text, or null when the save has no settings. A narrow row breaks it only
    /// between its parts, never inside one.
    /// </summary>
    public static Control? Line(SaveInfo save)
    {
        if (Parts(save) is not { } parts)
        {
            return null;
        }
        var line = new HFlowContainer { Name = "SaveSettings", MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        line.AddThemeConstantOverride("h_separation", 0);
        line.AddThemeConstantOverride("v_separation", 0);
        string join = UiText.T("load.settings.join");
        for (int i = 0; i < parts.Count; i++)
        {
            line.AddChild(UiFactory.Plain(i < parts.Count - 1 ? parts[i] + join : parts[i], PixelTheme.Variations.Muted));
        }
        return line;
    }
}
