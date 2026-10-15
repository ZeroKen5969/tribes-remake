using System;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Screens;
using Tribes.Core.World;

namespace Tribes.Client.Ui.Widgets;

/// <summary>
/// The choices of a new endless world: the start area, how many computer tribes start in it (no
/// maximum), how often new tribes appear as the world grows, the computers' difficulty (one for all
/// of them: their number has no limit) and the world's settings (<see cref="WorldOptions"/>). Shared
/// by the endless screen and the lobby of an endless LAN room (whose own row sets the difficulty).
/// </summary>
public partial class EndlessOptions : VBoxContainer
{
    private readonly OptionButton area;
    private readonly NumberStepper computers;
    private readonly OptionButton newTribes;
    private readonly OptionButton? difficulty;
    private readonly WorldOptions world;

    /// <summary>
    /// The rows, with <paramref name="computerTribes"/> computer tribes picked first; with
    /// <paramref name="withDifficulty"/> a row for the computers' difficulty.
    /// </summary>
    public EndlessOptions(int computerTribes = EndlessDefaults.ComputerTribes, bool withDifficulty = true)
    {
        Name = "EndlessOptions";
        AddThemeConstantOverride("separation", 6);
        area = UiFactory.Options(["endless.area_small", "endless.area_medium", "endless.area_large"],
            Array.IndexOf(EndlessDefaults.StartAreaSizes, EndlessDefaults.StartAreaSize), _ => { });
        area.Name = "StartArea";
        AddChild(UiFactory.Row("endless.start_area", area));
        // No maximum: any whole number from 0 that fits (the rules place the first ones at once, the rest as land opens).
        computers = new NumberStepper(Math.Max(0, computerTribes), 0) { Name = "ComputerTribes" };
        AddChild(UiFactory.Row("endless.computer_tribes", computers));
        if (withDifficulty)
        {
            difficulty = UiFactory.Options(["difficulty.easy", "difficulty.hard"], 0, _ => { });
            difficulty.Name = "Difficulty";
            AddChild(UiFactory.Row("endless.difficulty", difficulty));
        }
        newTribes = UiFactory.Options(["endless.new_tribes.none", "endless.new_tribes.few", "endless.new_tribes.steady", "endless.new_tribes.many"],
            EndlessSettings.SteadyNewTribes, _ => { });
        newTribes.Name = "NewTribes";
        AddChild(UiFactory.Row("endless.new_tribes", newTribes));
        AddChild(UiFactory.Label("endless.new_tribes_note", PixelTheme.Variations.Muted, wrap: true));
        world = new WorldOptions(EndlessDefaults.GraceMinutes);
        AddChild(world);
    }

    /// <summary>The chosen side of the start area, in cells.</summary>
    public int StartAreaSize => EndlessDefaults.StartAreaSizes[Math.Clamp(area.Selected, 0, EndlessDefaults.StartAreaSizes.Length - 1)];

    /// <summary>The chosen number of computer tribes at the start.</summary>
    public int ComputerTribes => computers.Value;

    /// <summary>The chosen density of new tribes (<see cref="EndlessSettings.NoNewTribes"/>..<see cref="EndlessSettings.ManyNewTribes"/>).</summary>
    public int NewTribes => Math.Max(0, newTribes.Selected);

    /// <summary>The chosen difficulty of every computer tribe (0 easy, 1 hard); 0 without the row.</summary>
    public int Difficulty => difficulty is null ? 0 : Math.Max(0, difficulty.Selected);

    /// <summary>The chosen world settings (opening grace, start stock, wild animals).</summary>
    public WorldSettings World => world.Settings;

    /// <summary>The settings of a new world with <paramref name="seed"/>.</summary>
    public EndlessSettings Settings(int seed) => new(seed, StartAreaSize, ComputerTribes, NewTribes);
}
