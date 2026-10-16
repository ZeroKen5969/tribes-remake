using System;
using System.Globalization;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Art;

namespace Tribes.Client.MatchPlay.Hud;

/// <summary>
/// The strip under the top bar: how many villagers gather food, wood, stones and bones, how many
/// build and how many builders wait, how many grown-ups stand idle, the children and everyone
/// else (<see cref="Workforce"/>). Pressing a count goes to the next villager
/// of that group in turn.
/// </summary>
public partial class WorkforceBar : PanelContainer
{
    /// <summary>A middle mood, whose plain face is the idle count's picture.</summary>
    private const int IdleFaceMood = 50;

    /// <summary>The building skill's icon, the builders' count's picture.</summary>
    private const int BuildingSkillIcon = 12;

    /// <summary>Room between two counts.</summary>
    private const int Separation = 2;

    private readonly IMatchActions actions;
    private readonly Button[] counts = new Button[Workforce.Groups];
    private GridContainer grid = null!;
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
        grid = new GridContainer { Columns = Workforce.Groups, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", Separation);
        grid.AddThemeConstantOverride("v_separation", Separation);
        AddChild(grid);
        Texture2D builder = HudArt.Inline(BuildingSkillIcon);
        Texture2D[] icons =
        {
            HudArt.Piece(HudArt.FoodPiece), HudArt.Piece(HudArt.WoodPiece), HudArt.Piece(HudArt.StonesPiece),
            HudArt.Piece(HudArt.BonesPiece), builder, HudArt.Greyed("builder", builder), HudArt.Mood(IdleFaceMood),
            HudArt.ChildFigure(), HudArt.Painted(PaintedIcon.Others),
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
            grid.AddChild(button);
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

    /// <summary>
    /// Lays the counts out in as few rows as fit in <paramref name="room"/> pixels across (two rows
    /// on a narrow phone), the rows as even as they can be.
    /// </summary>
    public void FitWidth(float room)
    {
        float widest = 0;
        foreach (Button button in counts)
        {
            widest = Math.Max(widest, button.GetCombinedMinimumSize().X);
        }
        float frame = GetCombinedMinimumSize().X - grid.GetCombinedMinimumSize().X;
        int fit = Math.Clamp((int)((room - frame + Separation) / (widest + Separation)), 1, Workforce.Groups);
        int rows = (Workforce.Groups + fit - 1) / fit;
        int columns = (Workforce.Groups + rows - 1) / rows;
        if (grid.Columns != columns)
        {
            grid.Columns = columns;
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
