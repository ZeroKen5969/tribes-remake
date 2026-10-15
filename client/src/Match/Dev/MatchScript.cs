using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Dev;
using Tribes.Client.World;
using Tribes.Core.Commands;
using Tribes.Core.Simulation;
using Tribes.Session;

namespace Tribes.Client.MatchPlay.Dev;

/// <summary>
/// Development autoload: plays a script of interface actions given with "--match-script=PATH"
/// (one command per line, # starts a comment), so whole flows can be checked without a person.
/// It lives across screens, so a script can start in the menus. Positions are logical screen
/// pixels; taps are sent as touches when the screen is laid out for touch, else as mouse clicks.
/// Commands (see <see cref="ScriptLine"/>): wait N, ticks N, until play|scene|over|results|placing|match|menu [frames],
/// tap X Y, rclick X Y, dtap X Y, drag X1 Y1 X2 Y2, tapcell X Y, rclickcell X Y, dtapcell X Y, tapmap PX PY, rclickmap PX PY,
/// tapunit N, tapbuilding KIND, press NAME|text:KEY, key NAME, zoom STEPS, centre X Y, capture PATH,
/// resize WxH, expect ..., cheat win|lose, dump, quit. Prints SCRIPT lines and ends with
/// SCRIPT_DONE ok or SCRIPT_FAIL.
/// </summary>
public partial class MatchScript : Node
{
    private const int DefaultUntilFrames = 3000;

    private readonly List<ScriptLine> lines = new();
    private readonly Dictionary<string, int> applied = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> refused = new(StringComparer.Ordinal);
    private int index;
    private int waitFrames;
    private long waitTicks = -1;
    private ScriptLine? until;
    private int untilLeft;
    private IMatchRunner? hooked;
    private bool failed;

    /// <summary>Orders this device's tribe had carried out, by kind (for expectations).</summary>
    public IReadOnlyDictionary<string, int> Applied => applied;

    /// <inheritdoc/>
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        string? path = DevOptions.Get("match-script");
        if (string.IsNullOrEmpty(path))
        {
            SetProcess(false);
            return;
        }
        string text = path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal)
            ? Godot.FileAccess.GetFileAsString(path) : System.IO.File.ReadAllText(path);
        lines.AddRange(ScriptLine.ParseAll(text));
        // "--ui-window=WxH" sizes the window for scripted runs too (the capture tool does it only for its own runs).
        string[] size = (DevOptions.Get("ui-window") ?? "").Split('x');
        if (size.Length == 2 && int.TryParse(size[0], out int width) && int.TryParse(size[1], out int height) && !DevOptions.Has("ui-capture"))
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetPosition(Vector2I.Zero);
            DisplayServer.WindowSetSize(new Vector2I(width, height));
        }
        GD.Print($"SCRIPT loaded {lines.Count} lines from {path}");
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        Hook();
        if (failed || index > lines.Count)
        {
            return;
        }
        if (waitFrames > 0)
        {
            waitFrames--;
            return;
        }
        if (waitTicks >= 0)
        {
            if (MatchScreen.Current is { IsOpen: true } screen && screen.Runner.Simulation.Tick < waitTicks)
            {
                return;
            }
            waitTicks = -1;
        }
        if (until is { } condition)
        {
            if (!Holds(condition.Args[0]))
            {
                if (--untilLeft <= 0)
                {
                    Fail($"until {condition.Args[0]} timed out");
                }
                return;
            }
            until = null;
        }
        if (index == lines.Count)
        {
            index++;
            GD.Print($"SCRIPT_DONE ok applied={string.Join(",", applied.Select(p => p.Key + ":" + p.Value))}");
            GetTree().Quit();
            return;
        }
        if (MatchScreen.Current is { IsOpen: true } open && open.Hud.Popup is Hud.HintDialog hint && lines[index].Command is not ("expect" or "capture" or "press" or "wait"))
        {
            // Hints come whenever the rules want; a script answers them like a player who reads and closes them.
            GD.Print($"SCRIPT hint {hint.Hint.Hint} closed");
            hint.Close();
            waitFrames = 2;
            return;
        }
        ScriptLine line = lines[index++];
        GD.Print($"SCRIPT {index}: {line}");
        try
        {
            Run(line);
            if (IsInput(line.Command) && waitFrames == 0)
            {
                // Input reaches the screen on the next frame and the panels follow on the one after.
                waitFrames = 2;
            }
        }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            Fail($"line {index} '{line}': {e.Message}");
        }
    }

    private void Hook()
    {
        IMatchRunner? runner = MatchScreen.Current is { IsOpen: true } screen ? screen.Runner : null;
        if (runner == hooked)
        {
            return;
        }
        if (hooked is not null)
        {
            hooked.TickRan -= OnTick;
        }
        hooked = runner;
        if (runner is not null)
        {
            runner.TickRan += OnTick;
        }
    }

    private void OnTick(object? sender, MatchTickEventArgs e)
    {
        for (int i = 0; i < e.LocalOrders.Count && i < e.Result.Outcomes.Count; i++)
        {
            if (e.LocalOrders[i] is { } order)
            {
                Dictionary<string, int> counts = e.Result.Outcomes[i] == OrderOutcome.Applied ? applied : refused;
                string kind = order.GetType().Name;
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
            }
        }
    }

    private void Run(ScriptLine line)
    {
        MatchScreen? screen = MatchScreen.Current is { IsOpen: true } s ? s : null;
        if (line.Command == "press" && !ReferenceEquals(centring, line))
        {
            // A control scrolled out of its list is scrolled into view first.
            centring = line;
            if (FindControl(line.Args[0]) is { } wanted && FindScroll(wanted) is { } scroll)
            {
                scroll.EnsureControlVisible(wanted);
                waitFrames = 2;
                index--;
                return;
            }
        }
        if (MovesCamera(line.Command) && !ReferenceEquals(centring, line))
        {
            // Commands aimed at something on the map first bring it on screen, then act a few
            // frames later, once the world view has drawn the new part of the map.
            centring = line;
            AimOnly(Need(screen), line);
            waitFrames = 3;
            index--;
            return;
        }
        centring = null;
        switch (line.Command)
        {
            case "wait":
                waitFrames = line.Int(0);
                break;
            case "ticks":
                waitTicks = Need(screen).Runner.Simulation.Tick + line.Int(0);
                break;
            case "until":
                until = line;
                untilLeft = line.Args.Count > 1 ? line.Int(1) : DefaultUntilFrames;
                break;
            case "tap":
                ScriptInput.Tap(this, line.Point(0), Touch(screen));
                break;
            case "dtap":
                ScriptInput.Tap(this, line.Point(0), Touch(screen));
                ScriptInput.Tap(this, line.Point(0), Touch(screen));
                break;
            case "rclick":
                ScriptInput.RightClick(this, line.Point(0));
                break;
            case "drag":
                ScriptInput.Drag(this, line.Point(0), line.Point(2), Touch(screen));
                break;
            case "tapcell":
            case "rclickcell":
            case "dtapcell":
            case "tapmap":
            case "rclickmap":
                Vector2 at = line.Command.EndsWith("map", StringComparison.Ordinal)
                    ? Need(screen).CameraRig.MapToScreen(new Vector2(line.Int(0), line.Int(1)))
                    : CellOnScreen(Need(screen), line.Int(0), line.Int(1));
                GD.Print($"SCRIPT {line.Command} at screen {at.X:0},{at.Y:0}");
                if (line.Command is "rclickcell" or "rclickmap")
                {
                    ScriptInput.RightClick(this, at);
                }
                else
                {
                    ScriptInput.Tap(this, at, Touch(screen));
                    if (line.Command == "dtapcell")
                    {
                        ScriptInput.Tap(this, at, Touch(screen));
                    }
                }
                break;
            case "tapunit":
                ScriptInput.Tap(this, UnitOnScreen(Need(screen), line.Int(0)), Touch(screen));
                break;
            case "tapbuilding":
                ScriptInput.Tap(this, BuildingOnScreen(Need(screen), line.Int(0), line.Args.Count > 1 ? line.Int(1) : -1), Touch(screen));
                break;
            case "tapevictable":
                ScriptInput.Tap(this, EvictableOnScreen(Need(screen)), Touch(screen));
                break;
            case "press":
                Control control = FindControl(line.Args[0]) ?? throw new InvalidOperationException($"no control {line.Args[0]}");
                ScriptInput.Tap(this, control.GetGlobalRect().GetCenter(), Touch(screen));
                break;
            case "key":
                ScriptInput.Key(line.Args[0]);
                break;
            case "zoom":
                Need(screen).CameraRig.ZoomSteps(line.Int(0), Need(screen).GetViewportRect().Size / 2);
                break;
            case "centre":
                Need(screen).CameraRig.CentreOnCell(line.Int(0), line.Int(1));
                break;
            case "capture":
                Capture(line.Args[0]);
                break;
            case "resize":
                string[] size = line.Args[0].Split('x');
                DisplayServer.WindowSetSize(new Vector2I(int.Parse(size[0], CultureInfo.InvariantCulture), int.Parse(size[1], CultureInfo.InvariantCulture)));
                waitFrames = 20;
                break;
            case "expect":
                Expect(screen, line);
                break;
            case "cheat":
                if (!Need(screen).DevCheat(line.Args[0], line.Args.Count > 1 ? line.Int(1) : 0, line.Args.Count > 2 ? line.Int(2) : 0))
                {
                    Fail($"cheat {line.Args[0]} not possible");
                }
                break;
            case "ghostfree":
                PlaceGhostFree(Need(screen));
                break;
            case "nearest":
            case "rnearest":
                Vector2 target = ScriptTargets.Nearest(Need(screen), line.Args[0]);
                if (line.Command == "rnearest")
                {
                    ScriptInput.RightClick(this, target);
                }
                else
                {
                    ScriptInput.Tap(this, target, Touch(screen));
                }
                break;
            case "dump":
                Dump(screen);
                break;
            case "skipscenes":
                MatchDev.SkipScenes = line.Args[0] == "on";
                break;
            case "deletesave":
                foreach (Tribes.Client.Ui.Game.SaveInfo save in Tribes.Client.Ui.Game.SaveCatalog.List())
                {
                    if (save.Mode == line.Args[0] && save.Slot == line.Args[1])
                    {
                        Tribes.Client.Ui.Game.SaveCatalog.Delete(save);
                    }
                }
                break;
            case "dumpunit":
                DumpUnit(Need(screen), line.Int(0));
                break;
            case "quit":
                index = lines.Count;
                break;
            default:
                throw new FormatException($"unknown command {line.Command}");
        }
    }

    private static bool Touch(MatchScreen? screen) => screen?.Metrics.Touch ?? (DisplayServer.IsTouchscreenAvailable() || MatchDev.ForceTouch);

    private static MatchScreen Need(MatchScreen? screen) => screen ?? throw new InvalidOperationException("no match screen");

    private bool Holds(string condition)
    {
        MatchScreen? screen = MatchScreen.Current is { IsOpen: true } s ? s : null;
        return condition switch
        {
            "match" => screen is not null,
            "menu" => screen is null && GetTree().CurrentScene is Tribes.Client.Ui.ScreenBase,
            "play" => screen is not null && screen.Runner.Simulation.CurrentScene is null && screen.Runner.Simulation.NextKind == TickKind.Play
                && screen.Hud.Popup is null,
            "scene" => screen?.Runner.Simulation.CurrentScene is not null,
            "over" => screen?.Runner.Simulation.IsOver == true,
            "campaignlist" => screen is null && GetTree().CurrentScene is Tribes.Client.Ui.Screens.CampaignScreen,
            "results" => screen?.Hud.Popup is Hud.ResultsPanel,
            "placing" => screen?.Overlay.Mode is OverlayMode.Building or OverlayMode.Trap,
            "popup" => screen?.Hud.Popup is not null,
            "nopopup" => screen?.Hud.Popup is null,
            "hint" => screen?.Hud.Popup is Hud.HintDialog,
            "inside" => screen is not null && BuildingOffers.LivingUnits(screen.World, Math.Max(0, screen.Tribe))
                .Any(id => BuildingOffers.BuildingInside(screen.World, screen.World.Unit(id)) != 0),
            "evictable" => screen is not null && BuildingOffers.StandingBuildings(screen.World, Math.Max(0, screen.Tribe))
                .Any(id => BuildingOffers.Evictable(screen.World, screen.World.Building(id)).Count > 0),
            _ => throw new FormatException($"unknown condition {condition}"),
        };
    }

    private void Expect(MatchScreen? screen, ScriptLine line)
    {
        string what = line.Args[0];
        bool ok = what switch
        {
            "units" => Need(screen).Selection.Units.Count >= line.Int(1),
            "building" => Need(screen).Selection.Kind == SelectionKind.Building,
            "nothing" => Need(screen).Selection.Kind == SelectionKind.None,
            "popup" => Need(screen).Hud.Popup?.Name == line.Args[1],
            "nopopup" => Need(screen).Hud.Popup is null,
            "applied" => applied.GetValueOrDefault(line.Args[1]) >= (line.Args.Count > 2 ? line.Int(2) : 1),
            "control" => FindControl(line.Args[1]) is { } c && c.IsVisibleInTree(),
            "nocontrol" => FindControl(line.Args[1]) is not { } shown || !shown.IsVisibleInTree(),
            "scene" => (Need(screen).Runner.Simulation.CurrentScene is not null) == (line.Args[1] == "on"),
            "placing" => Need(screen).Overlay.Mode is OverlayMode.Building or OverlayMode.Trap,
            "tribe" => Need(screen).Tribe == line.Int(1),
            "mission" => Need(screen).World.Mission == line.Int(1),
            "dialogue" => Need(screen).Runner.Simulation.CurrentScene?.Dialogue == line.Int(1),
            "completed" => Tribes.Client.Ui.Game.CampaignProgress.Load().IsCompleted(line.Int(1)),
            // Whether the ticks run held (the menus, hints, questions and slides hold them; the roster does not).
            "paused" => Need(screen).Runner.IsPaused == (line.Args[1] == "on"),
            "roster" => Need(screen).Runner.RosterOpen == (line.Args[1] == "on"),
            "minimap" => Need(screen).Hud.MinimapShown == (line.Args[1] == "on"),
            "file" => Godot.FileAccess.FileExists(line.Args[1]),
            "nofile" => !Godot.FileAccess.FileExists(line.Args[1]),
            _ => throw new FormatException($"unknown expectation {what}"),
        };
        if (!ok)
        {
            Fail($"expect {string.Join(' ', line.Args)} failed");
            Dump(screen);
        }
        else
        {
            GD.Print($"SCRIPT ok: {string.Join(' ', line.Args)}");
        }
    }

    private void Fail(string why)
    {
        failed = true;
        GD.Print($"SCRIPT_FAIL {why} applied={string.Join(",", applied.Select(p => p.Key + ":" + p.Value))} refused={string.Join(",", refused.Select(p => p.Key + ":" + p.Value))}");
        GetTree().Quit(1);
    }

    private void Capture(string path)
    {
        // A bare file name goes to "--match-script-out=DIR" (user://script-captures by default).
        if (!System.IO.Path.IsPathRooted(path) && !path.Contains("://", StringComparison.Ordinal))
        {
            string folder = DevOptions.Get("match-script-out") ?? "user://script-captures";
            DirAccess.MakeDirRecursiveAbsolute(folder);
            path = folder.TrimEnd('/') + "/" + path;
        }
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(path);
        GD.Print($"SCRIPT capture {path} {result} {image.GetWidth()}x{image.GetHeight()}");
    }

    private void Dump(MatchScreen? screen)
    {
        if (screen is null)
        {
            GD.Print($"SCRIPT dump scene={GetTree().CurrentScene?.Name}");
            return;
        }
        var world = screen.World;
        int tribe = Math.Max(0, screen.Tribe);
        GD.Print($"SCRIPT dump tick={screen.Runner.Simulation.Tick} frame={world.Frame} kind={screen.Runner.Simulation.NextKind} scene={screen.Runner.Simulation.CurrentScene?.Dialogue.ToString(CultureInfo.InvariantCulture) ?? "-"} "
            + $"popup={screen.Hud.Popup?.Name ?? "-"} selection={screen.Selection.Kind}:{string.Join('/', screen.Selection.Units)}:{screen.Selection.Building} "
            + $"units={BuildingOffers.LivingUnits(world, tribe).Count} buildings={BuildingOffers.StandingBuildings(world, tribe).Count} "
            + $"zoom={screen.CameraRig.Zoom:0.00} paused={screen.Runner.IsPaused} applied={string.Join(",", applied.Select(p => p.Key + ":" + p.Value))}");
        Godot.Vector2 centre = screen.CameraRig.VisibleMap().GetCenter();
        Godot.Rect2 bounds = screen.View.MapBounds;
        GD.Print($"SCRIPT dump camera tribe={screen.Tribe} viewTribe={screen.ViewTribe} viewer={screen.View.Looks.Viewer} "
            + $"centre={(int)Math.Floor(centre.X / WorldView.CellWidth)},{(int)Math.Floor(centre.Y / WorldView.CellHeight)} "
            + $"bounds={(int)(bounds.Position.X / WorldView.CellWidth)},{(int)(bounds.Position.Y / WorldView.CellHeight)}+{(int)(bounds.Size.X / WorldView.CellWidth)}x{(int)(bounds.Size.Y / WorldView.CellHeight)} "
            + $"home={screen.HomeCell(screen.ViewTribe)}");
        GD.Print("SCRIPT dump kinds " + string.Join(" ", Enumerable.Range(0, world.Tribes.Count)
            .Select(t => $"t{t}:" + string.Join(",", BuildingOffers.StandingBuildings(world, t).Select(id => world.Building(id).Kind)))));
        foreach (int id in screen.Selection.Units)
        {
            var u = world.Unit(id);
            GD.Print($"SCRIPT dump unit {id} at {u.X},{u.Y} action={u.Action} task={u.AssignedTask} dest={u.DestinationX},{u.DestinationY} carry={u.CarriedItem}x{u.CarriedAmount} happy={u.Happiness} inside={BuildingOffers.BuildingInside(world, u)}");
        }
        GD.Print($"SCRIPT dump buildings {string.Join(" ", BuildingOffers.StandingBuildings(world, tribe).Select(id => $"{id}:k{world.Building(id).Kind}L{world.Building(id).Level}/{world.UnlockLevel(Math.Clamp(world.Building(id).Kind, 0, 11))}@{world.Building(id).X},{world.Building(id).Y}in{BuildingOffers.Inside(world, world.Building(id)).Count}op{world.Building(id).OperatorOrBeds}"))} stock={string.Join(",", Enumerable.Range(0, 11).Select(c => world.Tribes[tribe].Counter(c)))}");
    }

    private static ScrollContainer? FindScroll(Node node)
    {
        for (Node? parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is ScrollContainer scroll)
            {
                return scroll;
            }
        }
        return null;
    }

    private static void DumpUnit(MatchScreen screen, int id)
    {
        var world = screen.World;
        var u = world.Unit(id);
        var cell = new Tribes.Core.Content.GridPoint(u.X, u.Y);
        string onCell = world.Map.Contains(cell) ? world.Map.CellAt(cell).ToString() : "-";
        GD.Print($"SCRIPT unit {id} at {u.X},{u.Y} cell={onCell} sub={u.ActionSubState} target={u.TargetIndex} slot={u.BuildingSlot} action={u.Action} hp={u.HitPoints} inside={BuildingOffers.BuildingInside(world, u)}");
    }

    private Control? FindControl(string name)
    {
        Node root = GetTree().Root;
        if (name.StartsWith("text:", StringComparison.Ordinal))
        {
            string key = name[5..];
            return FindAll(root).OfType<BaseButton>().OfType<Control>().FirstOrDefault(c => c.IsVisibleInTree() && c is Button b && b.Text == key);
        }
        if (name.StartsWith("prefix:", StringComparison.Ordinal))
        {
            string prefix = name[7..];
            return FindAll(root).OfType<Control>().FirstOrDefault(c => c.Name.ToString().StartsWith(prefix, StringComparison.Ordinal) && c.IsVisibleInTree());
        }
        return FindAll(root).OfType<Control>().FirstOrDefault(c => c.Name == name && c.IsVisibleInTree());
    }

    private static IEnumerable<Node> FindAll(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node inner in FindAll(child))
            {
                yield return inner;
            }
        }
    }

    private ScriptLine? centring;

    private static bool IsInput(string command) => command is "tap" or "dtap" or "rclick" or "drag" or "tapcell" or "rclickcell"
        or "dtapcell" or "tapmap" or "rclickmap" or "tapunit" or "tapbuilding" or "tapevictable" or "press" or "key" or "nearest" or "rnearest" or "ghostfree";

    private static bool MovesCamera(string command) => command is "tapunit" or "tapbuilding" or "nearest" or "rnearest" or "ghostfree"
        or "tapcell" or "rclickcell" or "dtapcell" or "tapevictable" or "tapmap" or "rclickmap";

    private static void AimOnly(MatchScreen screen, ScriptLine line)
    {
        switch (line.Command)
        {
            case "tapunit":
                UnitOnScreen(screen, line.Int(0));
                break;
            case "tapbuilding":
                BuildingOnScreen(screen, line.Int(0), line.Args.Count > 1 ? line.Int(1) : -1);
                break;
            case "tapevictable":
                EvictableOnScreen(screen);
                break;
            case "nearest":
            case "rnearest":
                ScriptTargets.Nearest(screen, line.Args[0]);
                break;
            case "ghostfree":
                ScriptTargets.FreeFootprint(screen, screen.PlacingKind ?? throw new InvalidOperationException("not placing"));
                break;
            case "tapmap":
            case "rclickmap":
                screen.CameraRig.CentreOn(new Vector2(line.Int(0), line.Int(1)));
                break;
            default:
                screen.CameraRig.CentreOnCell(line.Int(0), line.Int(1));
                break;
        }
    }

    private void PlaceGhostFree(MatchScreen screen)
    {
        if (screen.PlacingKind is not int kind)
        {
            throw new InvalidOperationException("not placing");
        }
        Vector2 at = ScriptTargets.FreeFootprint(screen, kind);
        ScriptInput.Tap(this, at, Touch(screen));
        if (Touch(screen))
        {
            // With a finger the first tap moves the ghost there and the second places it.
            ScriptInput.Tap(this, at, true);
        }
    }

    private static Vector2 CellOnScreen(MatchScreen screen, int x, int y) =>
        screen.CameraRig.MapToScreen(new Vector2((x + 0.5f) * WorldView.CellWidth, (y + 0.5f) * WorldView.CellHeight));

    private static Vector2 UnitOnScreen(MatchScreen screen, int n)
    {
        var world = screen.World;
        var units = BuildingOffers.LivingUnits(world, Math.Max(0, screen.Tribe))
            .Where(id => BuildingOffers.BuildingInside(world, world.Unit(id)) == 0).ToList();
        var unit = world.Unit(units[Math.Clamp(n, 0, units.Count - 1)]);
        screen.CameraRig.CentreOnCell(unit.X, unit.Y);
        return screen.CameraRig.MapToScreen(new Vector2((unit.X * WorldView.CellWidth) + unit.OffsetX + 11, (unit.Y * WorldView.CellHeight) + unit.OffsetY + 4));
    }

    private static Vector2 EvictableOnScreen(MatchScreen screen)
    {
        var world = screen.World;
        foreach (int id in BuildingOffers.StandingBuildings(world, Math.Max(0, screen.Tribe)))
        {
            var b = world.Building(id);
            if (BuildingOffers.Evictable(world, b).Count > 0)
            {
                // The cell one in from the top-left corner, or the only one of a 1 x 1 building (a tower).
                (int width, int height) = FootprintCheck.Size(world, Math.Clamp(b.Kind, 0, world.Content.Rules.Buildings.Kinds.Count - 1));
                int x = b.X + (width > 1 ? 1 : 0), y = b.Y + (height > 1 ? 1 : 0);
                screen.CameraRig.CentreOnCell(x, y);
                return screen.CameraRig.MapToScreen(new Vector2((x * WorldView.CellWidth) + 2, (y * WorldView.CellHeight) + 2));
            }
        }
        throw new InvalidOperationException("no building with someone to send out");
    }

    private static Vector2 BuildingOnScreen(MatchScreen screen, int kind, int tribe = -1)
    {
        var world = screen.World;
        foreach (int id in BuildingOffers.StandingBuildings(world, tribe >= 0 ? tribe : Math.Max(0, screen.Tribe)))
        {
            var b = world.Building(id);
            if (b.Kind == kind || (kind < 0 && RuleNumbers.IsSite(b.Kind)))
            {
                screen.CameraRig.CentreOnCell(b.X + 1, b.Y + 1);
                return screen.CameraRig.MapToScreen(new Vector2(((b.X + 1) * WorldView.CellWidth) + 2, ((b.Y + 1) * WorldView.CellHeight) + 2));
            }
        }
        throw new InvalidOperationException($"no building of kind {kind}");
    }
}
