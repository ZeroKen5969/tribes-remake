using System;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The strip under the top bar: how many villagers gather food, wood, stones and bones, and how
/// many grown-ups are idle (<see cref="Workforce"/>). Pressing a count goes to the next villager
/// of that group in turn.
/// </summary>
public partial class WorkforceBar : PanelContainer
{
    /// <summary>A middle mood, whose plain face is the idle count's picture.</summary>
    private const int IdleFaceMood = 50;

    private readonly IMatchActions actions;
    private readonly Button[] counts = new Button[Workforce.Groups];
    private int shownHash = int.MinValue;

    /// <summary>A strip for the match <paramref name="actions"/> plays.</summary>
    public WorkforceBar(IMatchActions actions)
    {
        this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>A group's count was pressed.</summary>
    public event Action<WorkGroup>? GroupPressed;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Name = "WorkforceBar";
        ThemeTypeVariation = PixelTheme.Variations.Inset;
        MouseFilter = MouseFilterEnum.Stop;
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 2);
        AddChild(row);
        Texture2D[] icons =
        {
            HudArt.Piece(HudArt.FoodPiece), HudArt.Piece(HudArt.WoodPiece), HudArt.Piece(HudArt.StonesPiece),
            HudArt.Piece(HudArt.BonesPiece), HudArt.Mood(IdleFaceMood),
        };
        for (int g = 0; g < Workforce.Groups; g++)
        {
            var group = (WorkGroup)g;
            var button = new Button
            {
                Name = "Workforce" + group,
                Icon = icons[g],
                Text = "0",
                TooltipText = "hud.workforce." + group.ToString().ToLowerInvariant(),
                ThemeTypeVariation = PixelTheme.Variations.Tool,
                FocusMode = FocusModeEnum.None,
            };
            button.Pressed += () => GroupPressed?.Invoke(group);
            counts[g] = button;
            row.AddChild(button);
        }
    }

    /// <summary>Sizes the counts for <paramref name="metrics"/> (bigger to tap on touch screens).</summary>
    public void Arrange(HudMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var size = new Vector2(metrics.Touch ? metrics.Button * 2 / 3 : 30, metrics.Touch ? metrics.TopBar : 18);
        foreach (Button button in counts)
        {
            button.CustomMinimumSize = size;
        }
    }

    /// <summary>The size the strip needs.</summary>
    public Vector2 WantedSize => GetCombinedMinimumSize();

    /// <summary>Shows the counts of the viewed tribe.</summary>
    public void Refresh()
    {
        int[] shown = Workforce.Counts(actions.World, actions.ViewTribe);
        var hash = new HashCode();
        foreach (int count in shown)
        {
            hash.Add(count);
        }
        int value = hash.ToHashCode();
        if (value == shownHash)
        {
            return;
        }
        shownHash = value;
        for (int g = 0; g < Workforce.Groups; g++)
        {
            counts[g].Text = shown[g].ToString(CultureInfo.InvariantCulture);
        }
    }
}
