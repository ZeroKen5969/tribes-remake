using System;
using Godot;
using Tribes.Client.Ui.Art;
using Tribes.Core.World;
using Tribes.Session;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// The settings of a new match or endless world that the rules keep (<see cref="WorldSettings"/>):
/// the opening grace (no computer tribe attacks anyone before it ends), the start stock and wild
/// animals. Shared by the endless screen and the lobby (versus and endless rooms).
/// </summary>
public partial class WorldOptions : VBoxContainer
{
    /// <summary>Opening grace lengths offered, in minutes at normal speed.</summary>
    public static readonly int[] GraceMinutes = [0, 2, 5, 10, 15, 20];

    /// <summary>Start stocks offered, in percent of the map's own.</summary>
    public static readonly int[] StockPercents = [WorldSettings.OriginalStock, 2 * WorldSettings.OriginalStock, 4 * WorldSettings.OriginalStock];

    private readonly OptionButton grace;
    private readonly OptionButton stock;
    private readonly OptionButton animals;

    /// <summary>The rows, with an opening grace of <paramref name="graceMinutes"/> picked first.</summary>
    public WorldOptions(int graceMinutes)
    {
        Name = "WorldOptions";
        AddThemeConstantOverride("separation", 6);
        grace = UiFactory.Options(Array.ConvertAll(GraceMinutes, m => $"world.grace.{m}"), Math.Max(0, Array.IndexOf(GraceMinutes, graceMinutes)), _ => { });
        grace.Name = "Grace";
        AddChild(UiFactory.Row("world.grace", grace));
        stock = UiFactory.Options(Array.ConvertAll(StockPercents, p => $"world.stock.{p}"), 0, _ => { });
        stock.Name = "Stock";
        AddChild(UiFactory.Row("world.stock", stock));
        animals = UiFactory.Options(["world.animals.on", "world.animals.off"], 0, _ => { });
        animals.Name = "Animals";
        AddChild(UiFactory.Row("world.animals", animals));
        AddChild(UiFactory.Label("world.grace_note", PixelTheme.Variations.Muted, wrap: true));
    }

    /// <summary>The chosen settings.</summary>
    public WorldSettings Settings => new()
    {
        OpeningGraceFrames = GraceMinutes[Math.Clamp(grace.Selected, 0, GraceMinutes.Length - 1)] * MatchPlan.FramesPerMinute,
        StartStockPercent = StockPercents[Math.Clamp(stock.Selected, 0, StockPercents.Length - 1)],
        WildAnimals = animals.Selected != 1,
    };
}
