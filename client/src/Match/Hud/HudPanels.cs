using System;
using Godot;
using Tribes.Client.Ui;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>Containers of the match panels, built the same way everywhere.</summary>
public static class HudPanels
{
    /// <summary>A vertical box that lets clicks through to its children only.</summary>
    public static VBoxContainer Column(int separation)
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>A horizontal box that lets clicks through to its children only.</summary>
    public static HBoxContainer Row(int separation)
    {
        var box = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", separation);
        return box;
    }

    /// <summary>A row that wraps onto more lines when it runs out of room.</summary>
    public static HFlowContainer Flow(int separation)
    {
        var flow = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", separation);
        flow.AddThemeConstantOverride("v_separation", separation);
        return flow;
    }

    /// <summary><paramref name="content"/> in a slot frame (faces, pictures).</summary>
    public static PanelContainer Framed(Control content)
    {
        ArgumentNullException.ThrowIfNull(content);
        PanelContainer panel = UiFactory.Panel(PixelTheme.Variations.Slot, content);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        return panel;
    }

    /// <summary>A heading line.</summary>
    public static Label Heading(string key)
    {
        Label label = UiFactory.Label(key, PixelTheme.Variations.Heading);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    /// <summary>A thin line between parts of a panel.</summary>
    public static HSeparator Separator() => new() { MouseFilter = Control.MouseFilterEnum.Ignore };
}
