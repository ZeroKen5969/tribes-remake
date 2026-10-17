using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.MatchPlay.Dev;
using Tribes.Client.MatchPlay.Hud;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Ui.Widgets;
using Tribes.Client.World;
using Tribes.Client.World.Scene;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Client.MatchPlay;

/// <summary>Taps, clicks, drags, pinches and keys on the map.</summary>
public partial class MatchScreen
{
    private const float TouchPickTolerance = 9f, MousePickTolerance = 3f;
    private const double GroupDoubleSeconds = 0.4;

    private bool multiSelect;
    private Rect2? box;
    private Vector2? hover;
    private int hoveredUnit;

    /// <summary>The building under the mouse pointer (shown as under the original's cursor), or 0.</summary>
    private int hoveredBuilding;

    private void WireGestures()
    {
        gestures.Panned += delta =>
        {
            DropBox();
            if (!SceneBlocksMap())
            {
                rig.PanBy(delta);
            }
        };
        gestures.Zoomed += (factor, focus) =>
        {
            DropBox();
            if (Mathf.IsEqualApprox(factor, gestures.WheelZoomStep) || Mathf.IsEqualApprox(factor, 1f / gestures.WheelZoomStep))
            {
                rig.ZoomSteps(factor > 1 ? 1 : -1, focus);
            }
            else
            {
                rig.ZoomBy(factor, focus);
            }
        };
        gestures.Tapped += position => OnTap(position, metrics.Touch ? (multiSelect ? TapMode.Toggle : TapMode.Auto)
            : Godot.Input.IsKeyPressed(Key.Shift) ? TapMode.Toggle : TapMode.Select, hurry: false);
        gestures.DoubleTapped += OnDoubleTap;
        gestures.SecondaryTapped += OnSecondaryTap;
        gestures.BoxChanged += rect =>
        {
            box = rect;
            hud.QueueRedrawBox(rect);
        };
        gestures.BoxSelected += rect =>
        {
            box = null;
            hud.QueueRedrawBox(null);
            OnBox(rect, add: multiSelect || Godot.Input.IsKeyPressed(Key.Shift));
        };
        gestures.LongPressed += _ =>
        {
            if (metrics.Touch && !SceneBlocksMap())
            {
                hud.Note(Tribes.Client.Ui.Text.UiText.T("hud.note.box"));
            }
        };
        // While placing, a finger put on the ghost drags it instead of the map.
        gestures.Grabs = GrabsGhost;
        gestures.GrabMoved += MoveGrabbedGhost;
        gestures.GrabReleased += ReleaseGrabbedGhost;
    }

    /// <summary>Whether the story scene (or a result) keeps the map from taking input.</summary>
    private bool SceneBlocksMap() => !opened || runner.Simulation.CurrentScene is not null || resultsShown;

    /// <summary>
    /// Whether a tap or box on the map is taken by an open panel instead: a question, or a panel
    /// that holds the game (menu, hint, build menu, names...). Such a tap closes the panel unless
    /// it needs an answer (hint, result).
    /// </summary>
    private bool PanelTakesMapTap()
    {
        if (ModalDialog.IsOpen(hud.Dialogs))
        {
            return true;
        }
        if (hud.Popup is not { Hold: not null } open)
        {
            return false;
        }
        if (open is not HintDialog and not ResultsPanel)
        {
            open.Close();
        }
        return true;
    }

    /// <summary>Forgets the selection box being drawn (a pinch or pan took over the gesture).</summary>
    private void DropBox()
    {
        if (box is null)
        {
            return;
        }
        box = null;
        hud.QueueRedrawBox(null);
    }

    private void OnTap(Vector2 screen, TapMode mode, bool hurry, bool secondTapGoesBehind = true)
    {
        if (SceneBlocksMap() || PanelTakesMapTap())
        {
            return;
        }
        Vector2 point = rig.ScreenToMap(screen);
        if (placing is not null || pickingPoint is not null || aimingSpell is not null)
        {
            TapInMode(point, mode);
            return;
        }
        MapPick pick = PickAt(point);
        if (secondTapGoesBehind && mode is TapMode.Select or TapMode.Auto && pick.IsCreature && TapRules.ShowsAlready(selection, pick)
            && MapPicker.BuildingAt(World, Math.Max(0, ViewTribe), Shapes, point.X, point.Y, WorldView.CellWidth, WorldView.CellHeight, Sight) is { } behind)
        {
            // A second tap on the villager (or animal) shown already: the building drawn behind it.
            MatchDev.Log($"TAP {mode} behind {behind.Kind} id={behind.Id}");
            if (behind.Kind == PickKind.OwnBuilding)
            {
                selection.SelectBuilding(behind.Id);
            }
            else
            {
                selection.ShowOther(behind.TargetCode);
            }
            return;
        }
        int kind = pick.Kind == PickKind.OwnBuilding ? World.Building(pick.Id).Kind : -1;
        bool units = selection.HasUnits && !Spectating;
        TapAction action = TapRules.Resolve(mode, units, pick, kind);
        MatchDev.Log($"TAP {mode} {pick.Kind} id={pick.Id} cell={pick.Cell.X},{pick.Cell.Y} -> {action}");
        switch (action)
        {
            case TapAction.SelectUnit:
                selection.SelectUnit(pick.Id);
                revealUnit = pick.Id;
                break;
            case TapAction.ToggleUnit:
                selection.ToggleUnit(pick.Id);
                break;
            case TapAction.SelectBuilding:
                selection.SelectBuilding(pick.Id);
                break;
            case TapAction.ShowOther:
                selection.ShowOther(pick.TargetCode);
                break;
            case TapAction.Deselect:
                selection.Clear();
                break;
            case TapAction.Move:
                Send(TapRules.Move(Tribe, selection, pick, hurry));
                Tribes.Client.Audio.SoundEffects.Instance?.Play(SoundCues.Order);
                overlay.ShowOrderFeedback(pick.Cell, pick.Kind is PickKind.OtherUnit or PickKind.Animal or PickKind.OtherBuilding,
                    TargetMarks.Mark(World, Tribe, Sight, pick.Cell));
                break;
            case TapAction.StoreErrand:
                OpenStore(pick.Id, pick.Cell);
                break;
        }
    }

    /// <summary>
    /// A second quick tap: with a finger, selected units run to a target (the original's double
    /// press); a double tap on open ground with nothing selected zooms in; anything else is a
    /// normal tap (so a double click still selects).
    /// </summary>
    private void OnDoubleTap(Vector2 screen)
    {
        if (SceneBlocksMap() || PanelTakesMapTap())
        {
            return;
        }
        if (Time.GetTicksMsec() / 1000.0 - modeEndedAt < DoubleClickSeconds + 0.1)
        {
            // The second tap of a double tap that placed something or picked a target is not an order:
            // with a finger it is dropped, with a mouse it is a plain click (which only selects).
            if (!metrics.Touch)
            {
                OnTap(screen, Godot.Input.IsKeyPressed(Key.Shift) ? TapMode.Toggle : TapMode.Select, hurry: false, secondTapGoesBehind: false);
            }
            return;
        }
        if (placing is not null || pickingPoint is not null || aimingSpell is not null)
        {
            OnTap(screen, metrics.Touch ? TapMode.Auto : TapMode.Select, hurry: false, secondTapGoesBehind: false);
            return;
        }
        MapPick pick = PickAt(rig.ScreenToMap(screen));
        if (metrics.Touch && selection.HasUnits && !Spectating && !multiSelect && pick.Kind is not (PickKind.OwnUnit or PickKind.Outside))
        {
            Send(TapRules.Move(Tribe, selection, pick, hurry: true));
            Tribes.Client.Audio.SoundEffects.Instance?.Play(SoundCues.Order);
            overlay.ShowOrderFeedback(pick.Cell, pick.Kind is PickKind.OtherUnit or PickKind.Animal or PickKind.OtherBuilding,
                TargetMarks.Mark(World, Tribe, Sight, pick.Cell));
            return;
        }
        if (selection.Kind == SelectionKind.None && pick.Kind is PickKind.Ground or PickKind.Terrain)
        {
            rig.ZoomSteps(1, screen);
            return;
        }
        OnTap(screen, metrics.Touch ? (multiSelect ? TapMode.Toggle : TapMode.Auto) : Godot.Input.IsKeyPressed(Key.Shift) ? TapMode.Toggle : TapMode.Select, hurry: false, secondTapGoesBehind: false);
    }

    /// <summary>A right click; a second one at about the same place soon after makes the units run.</summary>
    private void OnSecondaryTap(Vector2 screen)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        bool hurry = now - lastRightClick < DoubleClickSeconds && screen.DistanceTo(lastRightClickAt) < DoubleClickSlop;
        lastRightClick = hurry ? -10 : now;
        lastRightClickAt = screen;
        OnTap(screen, TapMode.Order, hurry);
    }

    private const double DoubleClickSeconds = 0.3;
    private const float DoubleClickSlop = 12f;
    private double lastRightClick = -10;
    private Vector2 lastRightClickAt;

    private void OnBox(Rect2 screenBox, bool add)
    {
        if (SceneBlocksMap() || Spectating || PanelTakesMapTap())
        {
            return;
        }
        Vector2 a = rig.ScreenToMap(screenBox.Position), b = rig.ScreenToMap(screenBox.End);
        List<int> picked = MapPicker.UnitsInBox(World, Tribe, Boxes(), a.X, a.Y, b.X, b.Y);
        if (add)
        {
            selection.AddUnits(picked);
        }
        else
        {
            selection.SetUnits(picked);
        }
        MatchDev.Log($"BOX_SELECT {picked.Count} screen={screenBox} map={a}-{b}");
    }

    private MapPick PickAt(Vector2 point) =>
        MapPicker.Pick(World, Math.Max(0, ViewTribe), Boxes(), point.X, point.Y,
            (metrics.Touch ? TouchPickTolerance : MousePickTolerance) / rig.Zoom, WorldView.CellWidth, WorldView.CellHeight, Sight, Shapes);

    /// <summary>Where buildings are drawn, for taps on their pictures.</summary>
    private BuildingShapes Shapes => shapes ??= new BuildingShapes(WorldArt.Shared.Assets.Composers.Objects);

    private BuildingShapes? shapes;

    /// <summary>The creatures' boxes where they are drawn now (worked out once per frame).</summary>
    private List<CreatureBox> Boxes()
    {
        ulong frame = Engine.GetProcessFrames();
        if (boxes is null || boxesFrame != frame)
        {
            boxes = MapPicker.Boxes(view.SceneItems, view.ComposedView, WorldView.CellWidth, WorldView.CellHeight, view.MoverOffset);
            boxesFrame = frame;
        }
        return boxes;
    }

    private List<CreatureBox>? boxes;
    private ulong boxesFrame;

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!opened)
        {
            return;
        }
        switch (@event)
        {
            case InputEventMouseMotion motion when motion.Device != (int)InputEvent.DeviceIdEmulation:
                hover = motion.Position;
                OnHover(motion.Position);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                if (OnKey(key))
                {
                    GetViewport().SetInputAsHandled();
                }
                break;
        }
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationWMGoBackRequest when opened:
                Back();
                break;
            case NotificationWMMouseEnter:
                mouseInWindow = true;
                break;
            case NotificationWMMouseExit:
                mouseInWindow = false;
                break;
        }
    }

    private bool mouseInWindow = true;

    /// <summary>The pointer for scrolling at the screen edge: none with touch, outside the window, unfocused or under a panel.</summary>
    private Vector2? EdgeScrollMouse()
    {
        if (metrics.Touch || !mouseInWindow || !DisplayServer.WindowIsFocused() || hud.Popup is not null || ModalDialog.IsOpen(hud.Dialogs))
        {
            return null;
        }
        return GetViewport().GetMousePosition();
    }

    /// <summary>Esc / Android back: close a question, stop placing, close a panel, deselect, or open the menu.</summary>
    private void Back()
    {
        if (ModalDialog.IsOpen(hud.Dialogs))
        {
            ModalDialog.CloseTop(hud.Dialogs);
            return;
        }
        if (placing is not null || pickingPoint is not null || aimingSpell is not null)
        {
            CancelMode();
            return;
        }
        if (hud.Popup is { } open)
        {
            if (open is not HintDialog and not ResultsPanel)
            {
                open.Close();
            }
            return;
        }
        if (selection.Kind != SelectionKind.None)
        {
            selection.Clear();
            return;
        }
        if (!SceneBlocksMap())
        {
            OpenMenu();
        }
    }

    private void OnHover(Vector2 screen)
    {
        if (SceneBlocksMap())
        {
            return;
        }
        Vector2 point = rig.ScreenToMap(screen);
        if (placing is not null || pickingPoint is not null || aimingSpell is not null)
        {
            hoveredBuilding = 0;
            HoverInMode(point);
            return;
        }
        MapPick pick = PickAt(point);
        hoveredUnit = pick.IsCreature && pick.Kind != PickKind.Animal ? pick.Id : 0;
        hoveredBuilding = !metrics.Touch && pick.Kind is PickKind.OwnBuilding or PickKind.OtherBuilding ? pick.Id : 0;
        ShowPointer(pick);
    }

    /// <summary>With a mouse and units selected, the original's cursor and target mark on the cell under the pointer.</summary>
    private void ShowPointer(MapPick pick)
    {
        if (metrics.Touch || Spectating || Tribe < 0 || !selection.HasUnits || pick.Kind == PickKind.Outside)
        {
            overlay.HidePointer();
            return;
        }
        overlay.ShowPointer(pick.Cell, TargetMarks.Cursor(World, Tribe, Sight, pick.Cell), TargetMarks.Mark(World, Tribe, Sight, pick.Cell));
    }

    /// <summary>Keyboard shortcuts (also listed in the help).</summary>
    private bool OnKey(InputEventKey key)
    {
        Key code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        if (code == Key.Escape)
        {
            Back();
            return true;
        }
        if (ModalDialog.IsOpen(hud.Dialogs))
        {
            return false;
        }
        if (runner.Simulation.CurrentScene is not null)
        {
            if (code is Key.Enter or Key.KpEnter or Key.Space)
            {
                ContinueScene();
                return true;
            }
            return false;
        }
        if (hud.Popup is not null && code is not (Key.F1 or Key.Space or Key.P or Key.Tab))
        {
            return false;
        }
        bool ctrl = key.CtrlPressed || key.MetaPressed;
        switch (code)
        {
            case Key.Space or Key.P:
                PickSpeed(0);
                return true;
            case Key.Bracketleft:
                PickSpeed(Math.Max(100, runner.SpeedPercent / 2));
                return true;
            case Key.Bracketright:
                PickSpeed(Math.Min(400, runner.SpeedPercent * 2));
                return true;
            case Key.Equal or Key.KpAdd:
                rig.ZoomSteps(1, GetViewportRect().Size / 2);
                return true;
            case Key.Minus or Key.KpSubtract:
                rig.ZoomSteps(-1, GetViewportRect().Size / 2);
                return true;
            case >= Key.Key1 and <= Key.Key4:
                Group((int)(code - Key.Key1), ctrl);
                return true;
            case Key.C or Key.Home:
                CentreOnSelection();
                return true;
            case Key.B:
                OnCommand(HudCommand.Build);
                return true;
            case Key.R:
                OnCommand(HudCommand.Roster);
                return true;
            case Key.G:
                OnCommand(HudCommand.Army);
                return true;
            case Key.M:
                OnCommand(HudCommand.Minimap);
                return true;
            case Key.H or Key.F1:
                OnCommand(HudCommand.Help);
                return true;
            case Key.O:
                OnCommand(HudCommand.Objective);
                return true;
            case Key.Delete:
                DemolishSelected();
                return true;
            case Key.Tab when runner.IsNetwork:
                hud.Players.Toggle();
                return true;
        }
        return false;
    }

    /// <summary>Ctrl+1..4 stores the selection as a group; 1..4 selects it (pressed twice quickly, shows it).</summary>
    private void Group(int group, bool store)
    {
        if (store)
        {
            selection.StoreGroup(group);
            hud.Note(Tribes.Client.Ui.Text.UiText.F("hud.note.group_stored", group + 1));
            return;
        }
        double now = Time.GetTicksMsec() / 1000.0;
        if (!selection.RecallGroup(group, unit => MapPicker.IsOwnLivingUnit(World, Tribe, unit) && BuildingOffers.BuildingInside(World, World.Unit(unit)) == 0))
        {
            return;
        }
        if (lastCentreGroup == group && now - lastCentreGroupTime < GroupDoubleSeconds)
        {
            CentreOnSelection();
        }
        lastCentreGroup = group;
        lastCentreGroupTime = now;
    }

    private void DemolishSelected()
    {
        if (selection.Kind != SelectionKind.Building || Spectating)
        {
            return;
        }
        int building = selection.Building;
        ModalDialog.Show(hud.Dialogs, GameTexts.T(36),
            (Tribes.Client.Ui.UiKeys.Yes, () => Send(new DemolishBuilding(Tribe, building))), (Tribes.Client.Ui.UiKeys.No, null));
    }

    /// <summary>The camera direction held on the keyboard (arrows and WASD).</summary>
    private Vector2 KeyPan()
    {
        if (metrics.Touch || hud.Popup is not null || ModalDialog.IsOpen(hud.Dialogs) || GetViewport().GuiGetFocusOwner() is LineEdit)
        {
            return Vector2.Zero;
        }
        var direction = Vector2.Zero;
        direction.X -= Godot.Input.IsPhysicalKeyPressed(Key.Left) || Godot.Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0;
        direction.X += Godot.Input.IsPhysicalKeyPressed(Key.Right) || Godot.Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0;
        direction.Y -= Godot.Input.IsPhysicalKeyPressed(Key.Up) || Godot.Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0;
        direction.Y += Godot.Input.IsPhysicalKeyPressed(Key.Down) || Godot.Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0;
        return direction;
    }

    /// <summary>Gives the world view the marks of the selection, the box being drawn and the pointer.</summary>
    private void UpdateMarks()
    {
        if (!selection.HasUnits)
        {
            overlay.HidePointer();
        }
        var boxed = new HashSet<int>();
        if (box is { } rect)
        {
            Vector2 a = rig.ScreenToMap(rect.Position), c = rig.ScreenToMap(rect.End);
            boxed.UnionWith(MapPicker.UnitsInBox(World, Math.Max(0, Tribe), Boxes(), a.X, a.Y, c.X, c.Y));
        }
        // The scene is composed again only when the units in the box change, not on every drag frame.
        int boxedHash = boxed.Count;
        foreach (int unit in boxed)
        {
            boxedHash += HashCode.Combine(unit);
        }
        int taskIcons = Math.Clamp(Tribes.Client.App.GameApp.Instance.Settings.TaskIcons, 0, 2);
        int hash = HashCode.Combine(selection.Version, hoveredUnit, hoveredBuilding, boxedHash, World.Frame, taskIcons);
        if (hash == shownSelectionVersion)
        {
            return;
        }
        shownSelectionVersion = hash;
        var selected = new HashSet<int>(selection.Units);
        var icons = new Dictionary<int, int>();
        foreach (int unit in selected)
        {
            icons[unit] = TaskClass(World.Unit(unit));
        }
        view.Marks = new WorldMarks
        {
            SelectedUnits = selected,
            FocusedUnits = selected,
            HoveredUnit = hoveredUnit,
            BoxedUnits = boxed,
            TaskIconMode = taskIcons,
            TaskIcons = icons,
            PointedUnit = selection.Units.Count == 1 ? selection.Units[0] : 0,
            // What the original shows for the building under its cursor: the selected one, and with a mouse the one pointed at.
            PointedBuildings = BuildingFrame.Pointed(selection, metrics.Touch ? 0 : hoveredBuilding),
        };
        // The player's selected building, or another tribe's being looked at; with a mouse also the one pointed at.
        int marked = BuildingFrame.MarkedBuilding(selection);
        overlay.MarkedBuilding = marked > 0 && marked <= World.BuildingSlots ? Footprint(World.Building(marked)) : null;
        int hovered = metrics.Touch ? 0 : hoveredBuilding;
        overlay.HoveredBuilding = hovered > 0 && hovered != marked && hovered <= World.BuildingSlots ? Footprint(World.Building(hovered)) : null;
    }

    /// <summary>The task class (0..16) of a unit's long-term task, which picks its task icon.</summary>
    private int TaskClass(Unit unit)
    {
        var actions = World.Content.Rules.Actions.Actions;
        int task = unit.AssignedTask;
        return task >= 0 && task < actions.Count ? Math.Clamp(actions[task].Category, 0, LastTaskClass) : 0;
    }

    private const int LastTaskClass = 16;

    private Rect2I Footprint(Building b)
    {
        (int width, int height) = FootprintCheck.Size(World, Math.Clamp(b.Kind, 0, World.Content.Rules.Buildings.Kinds.Count - 1));
        return new Rect2I(b.X, b.Y, width, height);
    }

    /// <summary>Drops from the selection what is gone or went inside a building.</summary>
    private void PruneSelection()
    {
        GameWorld world = World;
        // A spectator keeps what they picked of the tribe they watch.
        int tribe = Spectating ? ViewTribe : Tribe;
        selection.Prune(
            unit => MapPicker.IsOwnLivingUnit(world, tribe, unit) && BuildingOffers.BuildingInside(world, world.Unit(unit)) == 0,
            building => building >= 1 && building <= world.BuildingSlots && world.Building(building).Stands && world.Building(building).Tribe == tribe,
            code => OtherCards.StillThere(world, code) && MapPicker.StillInSight(world, tribe, Sight, code));
    }

    /// <summary>
    /// Brings the bars and panels up to date when something they show may have changed: after
    /// ticks, a new selection, or a quarter of a second (pause state, players).
    /// </summary>
    private void RefreshHud()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        long tick = runner.Simulation.Tick;
        if (tick == hudTick && selection.Version == hudSelection && now - hudTime < HudRefreshSeconds)
        {
            return;
        }
        hudTick = tick;
        hudSelection = selection.Version;
        hudTime = now;
        hud.Top.Refresh(runner.SpeedPercent, ShownPaused || runner.IsPaused);
        hud.Workers.Refresh();
        hud.Commands.SetToggled(HudCommand.Pause, ShownPaused);
        hud.Context.Refresh();
        hud.Players.Refresh();
    }

    private const double HudRefreshSeconds = 0.25;
    private long hudTick = -1;
    private int hudSelection = -1;
    private double hudTime;
}
