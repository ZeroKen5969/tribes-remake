using System;
using Godot;
using Tribes.Client.App;
using Tribes.Client.MatchPlay.Dev;
using Tribes.Client.MatchPlay.Hud;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Session;
using Tribes.Client.Ui;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;
using Tribes.Client.World;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.World;
using Tribes.Net;
using Tribes.Session;

namespace Tribes.Client.MatchPlay;

/// <summary>What the interface asks of the screen: orders, placing and picking, panels and menus.</summary>
public partial class MatchScreen
{
    private const int PlaceBuildingPrompt = 33, PickAVillagerPrompt = 34, PickAPlacePrompt = 35;
    private const int VictoryCountdownStart = 1, DefeatCountdownStart = 50;

    /// <summary>How far (cells) around the camp the test help explores.</summary>
    private const int TestExploreRadius = 192;

    private (int Kind, int Armoury)? placing;
    private GridPoint ghostOrigin;
    private ArmyCall? pickingPoint;
    private (int Sanctuary, int Spell)? aimingSpell;
    private bool menuOpening;
    private double modeEndedAt = -10;
    private Pixels? thumbnail;

    /// <summary>The building or trap kind being placed, or null.</summary>
    public int? PlacingKind => placing?.Kind;

    /// <summary>The top-left cell of the placement ghost.</summary>
    public GridPoint GhostOrigin => ghostOrigin;

    /// <inheritdoc/>
    public void Send(GameOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (Spectating)
        {
            hud.Note(UiText.T("hud.note.spectating"));
            return;
        }
        OrderSubmitResult result = runner.Orders.Submit(order);
        MatchDev.Log($"ORDER_SENT {order.GetType().Name} {result}");
        if (result != OrderSubmitResult.Queued)
        {
            hud.Note(UiText.T("hud.submit." + result));
        }
    }

    /// <inheritdoc/>
    public void BeginPlacement(int kind)
    {
        CancelMode();
        placing = (kind, 0);
        StartGhost(GameTexts.T(PlaceBuildingPrompt));
    }

    /// <inheritdoc/>
    public void BeginTrap(int armoury, int trapKind)
    {
        CancelMode();
        placing = (trapKind, armoury);
        StartGhost(GameTexts.T(PickAPlacePrompt));
    }

    /// <inheritdoc/>
    public void BeginArmyPoint(ArmyCall call)
    {
        CancelMode();
        pickingPoint = call;
        hud.Placement.Begin(GameTexts.T(PickAPlacePrompt), showConfirm: false);
        hud.Relayout();
    }

    /// <inheritdoc/>
    public void BeginSpell(int sanctuary, int spell)
    {
        CancelMode();
        aimingSpell = (sanctuary, spell);
        bool onPlace = spell == OrderMenus.PlaceSpell;
        hud.Placement.Begin(GameTexts.T(onPlace ? PickAPlacePrompt : PickAVillagerPrompt), showConfirm: false);
        hud.Relayout();
    }

    /// <inheritdoc/>
    public void CancelMode()
    {
        if (placing is null && pickingPoint is null && aimingSpell is null)
        {
            return;
        }
        placing = null;
        pickingPoint = null;
        aimingSpell = null;
        modeEndedAt = Time.GetTicksMsec() / 1000.0;
        overlay.Clear();
        hud.Placement.End();
        hud.Relayout();
    }

    /// <inheritdoc/>
    public void DragPlacement(int kind, int finger, Vector2 screen)
    {
        BeginPlacement(kind);
        (_, int height) = FootprintCheck.Size(World, kind);
        Vector2 map = rig.ScreenToMap(screen);
        System.Numerics.Vector2 middle = GhostDrag.AboveFinger(new System.Numerics.Vector2(map.X, map.Y), height, WorldView.CellHeight);
        ghostOrigin = FootprintCheck.OriginUnder(World, kind, middle.X, middle.Y, WorldView.CellWidth, WorldView.CellHeight);
        ghostHold = GhostDrag.Hold(ghostOrigin, CellAt(map));
        RefreshGhost();
        draggedOutAt = null;
        if (finger >= 0)
        {
            gestures.TakeOverGrab(finger, screen);
            draggedOutAt = screen;
        }
        MatchDev.Log($"GHOST_DRAG out kind={kind} at={ghostOrigin.X},{ghostOrigin.Y} held={finger >= 0}");
    }

    /// <summary>How the finger dragging the ghost holds it (the finger's cell less the ghost's top-left cell).</summary>
    private GridPoint ghostHold;

    /// <summary>Fastest the view scrolls while the ghost is dragged against an edge, in screen pixels a second.</summary>
    private const float DragScrollSpeed = 360;

    /// <summary>
    /// Where the finger came out of the build menu with a ghost: it comes out at the menu's edge,
    /// which can be near an edge of the map part of the screen, so the view only scrolls once the
    /// finger has moved on (out of the edge band, or a button's width away).
    /// </summary>
    private Vector2? draggedOutAt;

    /// <summary>
    /// Whether a finger going down at <paramref name="screen"/> takes hold of the ghost: with a
    /// finger, while placing, on the ghost or just beside it (the finger's slack).
    /// </summary>
    private bool GrabsGhost(Vector2 screen)
    {
        if (!metrics.Touch || placing is not { } p || SceneBlocksMap() || hud.Popup is not null || ModalDialog.IsOpen(hud.Dialogs))
        {
            return false;
        }
        Vector2 map = rig.ScreenToMap(screen);
        (int width, int height) = FootprintCheck.Size(World, p.Kind);
        var ghost = new Rect2(ghostOrigin.X * WorldView.CellWidth, ghostOrigin.Y * WorldView.CellHeight, width * WorldView.CellWidth, height * WorldView.CellHeight)
            .Grow(TouchPickTolerance / rig.Zoom);
        if (!ghost.HasPoint(map))
        {
            return false;
        }
        ghostHold = GhostDrag.Hold(ghostOrigin, CellAt(map));
        draggedOutAt = null;
        return true;
    }

    /// <summary>The finger dragging the ghost is at <paramref name="screen"/>: the ghost follows it.</summary>
    private void MoveGrabbedGhost(Vector2 screen)
    {
        if (placing is null)
        {
            return;
        }
        GridPoint origin = GhostDrag.Follow(CellAt(rig.ScreenToMap(screen)), ghostHold);
        if (origin != ghostOrigin)
        {
            ghostOrigin = origin;
            RefreshGhost();
        }
    }

    /// <summary>The finger dragging the ghost was lifted: the ghost stays there until the tick places it.</summary>
    private void ReleaseGrabbedGhost(Vector2 screen)
    {
        MoveGrabbedGhost(screen);
        MatchDev.Log($"GHOST_DRAG released at={ghostOrigin.X},{ghostOrigin.Y} free={overlay.AllFree}");
    }

    /// <summary>While the ghost is dragged near an edge of the map part of the screen, the view scrolls that way and the ghost stays under the finger.</summary>
    private void ScrollWhileDragging(double delta)
    {
        if (placing is null || !gestures.IsGrabbing)
        {
            return;
        }
        Rect2 free = hud.FreeArea;
        Vector2 at = gestures.GrabPosition;
        System.Numerics.Vector2 speed = GhostDrag.EdgeScroll(new System.Numerics.Vector2(at.X, at.Y),
            new System.Numerics.Vector2(free.Position.X, free.Position.Y), new System.Numerics.Vector2(free.End.X, free.End.Y), metrics.Button, DragScrollSpeed);
        if (draggedOutAt is { } from)
        {
            if (speed != System.Numerics.Vector2.Zero && at.DistanceTo(from) < metrics.Button)
            {
                return;
            }
            draggedOutAt = null;
        }
        if (speed == System.Numerics.Vector2.Zero)
        {
            return;
        }
        rig.PanBy(new Vector2(-speed.X, -speed.Y) * (float)delta);
        MoveGrabbedGhost(at);
    }

    private void StartGhost(string prompt)
    {
        Rect2 free = hud.FreeArea;
        Vector2 middle = rig.ScreenToMap(free.Position + (free.Size / 2));
        ghostOrigin = FootprintCheck.OriginUnder(World, placing!.Value.Kind, middle.X, middle.Y, WorldView.CellWidth, WorldView.CellHeight);
        hud.Placement.Begin(prompt, showConfirm: metrics.Touch);
        hud.Relayout();
        RefreshGhost();
    }

    /// <summary>Shows the ghost where it is now, with each cell's state as the world is now.</summary>
    private void RefreshGhost()
    {
        if (placing is not { } p)
        {
            return;
        }
        var cells = FootprintCheck.Cells(World, Math.Max(0, Tribe), p.Kind, ghostOrigin);
        overlay.ShowPlacement(p.Kind, ghostOrigin, cells);
        hud.Placement.SetConfirmable(overlay.AllFree);
    }

    private void HoverInMode(Vector2 point)
    {
        if (placing is { } p)
        {
            GridPoint origin = FootprintCheck.OriginUnder(World, p.Kind, point.X, point.Y, WorldView.CellWidth, WorldView.CellHeight);
            if (origin != ghostOrigin)
            {
                ghostOrigin = origin;
                RefreshGhost();
            }
            return;
        }
        GridPoint cell = CellAt(point);
        if (pickingPoint is not null)
        {
            overlay.ShowTarget(OverlayMode.ArmyPoint, cell);
        }
        else if (aimingSpell is { } s)
        {
            overlay.ShowTarget(OverlayMode.SpellTarget, cell, s.Spell);
        }
    }

    private void TapInMode(Vector2 point, TapMode mode)
    {
        if (mode == TapMode.Order)
        {
            CancelMode();
            return;
        }
        if (placing is { } p)
        {
            GridPoint origin = FootprintCheck.OriginUnder(World, p.Kind, point.X, point.Y, WorldView.CellWidth, WorldView.CellHeight);
            if (metrics.Touch && origin != ghostOrigin)
            {
                // With a finger the first tap moves the ghost; the tick (or a tap on the ghost) places it.
                ghostOrigin = origin;
                RefreshGhost();
                return;
            }
            ghostOrigin = origin;
            RefreshGhost();
            ConfirmMode();
            return;
        }
        GridPoint cell = CellAt(point);
        if (pickingPoint is { } call)
        {
            Send(new CallArmy(Tribe, call, cell));
            overlay.ShowOrderFeedback(cell, attack: false);
            CancelMode();
            return;
        }
        if (aimingSpell is { } s)
        {
            if (s.Spell == OrderMenus.PlaceSpell)
            {
                Send(new CastSpellAt(Tribe, s.Sanctuary, s.Spell, cell));
                CancelMode();
                return;
            }
            MapPick pick = PickAt(point);
            if (pick.Kind == PickKind.OwnUnit && World.Unit(pick.Id).Age >= RuleNumbers.AdultAge)
            {
                Send(new CastSpell(Tribe, s.Sanctuary, s.Spell, pick.Id));
                CancelMode();
                return;
            }
            hud.Note(GameTexts.T(PickAVillagerPrompt));
        }
    }

    /// <summary>The tick of the placement bar: places the ghost where it is.</summary>
    private void ConfirmMode()
    {
        if (placing is not { } p)
        {
            return;
        }
        if (!overlay.AllFree)
        {
            hud.Note(UiText.T("hud.note.cannot_build"));
            return;
        }
        GameOrder order = RuleNumbers.IsTrap(p.Kind)
            ? new PlaceTrap(Tribe, p.Armoury, p.Kind, ghostOrigin)
            : new PlaceBuilding(Tribe, p.Kind, ghostOrigin);
        Send(order);
        Tribes.Client.Audio.SoundEffects.Instance?.Play(SoundCues.Place);
        if (!Godot.Input.IsKeyPressed(Key.Shift))
        {
            CancelMode();
        }
    }

    private static GridPoint CellAt(Vector2 point) =>
        new(Mathf.FloorToInt(point.X / WorldView.CellWidth), Mathf.FloorToInt(point.Y / WorldView.CellHeight));

    /// <inheritdoc/>
    public void CentreOnCell(int x, int y) => rig.CentreOnCell(x, y, glide: true);

    /// <inheritdoc/>
    public void CentreOnSelection()
    {
        GameWorld world = World;
        switch (selection.Kind)
        {
            case SelectionKind.Units:
                Unit first = world.Unit(selection.Units[0]);
                rig.CentreOn(new Vector2((first.X * WorldView.CellWidth) + first.OffsetX + 11, (first.Y * WorldView.CellHeight) + first.OffsetY + 8), glide: true);
                return;
            case SelectionKind.Building:
                Building b = world.Building(selection.Building);
                (int width, int height) = FootprintCheck.Size(world, Math.Clamp(b.Kind, 0, world.Content.Rules.Buildings.Kinds.Count - 1));
                rig.CentreOn(new Vector2((b.X + (width / 2f)) * WorldView.CellWidth, (b.Y + (height / 2f)) * WorldView.CellHeight), glide: true);
                return;
            default:
                (int x, int y) = HomeCell(ViewTribe);
                rig.CentreOnCell(x, y, glide: true);
                return;
        }
    }

    /// <inheritdoc/>
    public void SelectUnit(int unit, bool centre)
    {
        selection.SelectUnit(unit);
        if (centre)
        {
            CentreOnSelection();
        }
    }

    /// <summary>Room (map pixels at zoom 1) kept between a revealed villager and the panel's edge.</summary>
    private const float RevealMargin = 16;

    /// <summary>A villager just picked on the map, to bring out from under the panel once it shows them.</summary>
    private int? revealUnit;

    /// <summary>
    /// When the panel that opened for a villager picked on the map covers them, the view moves the
    /// least it takes (up, or to the side) to bring them out beside it. Waits until the panel shows
    /// that selection; dropped when the selection changes first.
    /// </summary>
    private void RevealSelected()
    {
        if (revealUnit is not int id)
        {
            return;
        }
        if (selection.Kind != SelectionKind.Units || selection.Units.Count != 1 || selection.Units[0] != id)
        {
            revealUnit = null;
            return;
        }
        ContextPanel panel = hud.Context;
        if (!panel.Visible || panel.ShownSelectionVersion != selection.Version)
        {
            return;
        }
        revealUnit = null;
        Unit unit = World.Unit(id);
        Vector2 point = rig.MapToScreen(new Vector2((unit.X * WorldView.CellWidth) + unit.OffsetX + 11, (unit.Y * WorldView.CellHeight) + unit.OffsetY + 8));
        Rect2 covered = panel.GetGlobalRect().Grow(RevealMargin * rig.Zoom);
        if (!covered.HasPoint(point))
        {
            return;
        }
        float up = point.Y - covered.Position.Y, aside = covered.End.X - point.X;
        Vector2 move = up <= aside ? new Vector2(0, -up) : new Vector2(aside, 0);
        MatchDev.Log($"REVEAL unit={id} move={move.X:0},{move.Y:0}");
        rig.PanBy(move);
    }

    /// <summary>The villager each head count last went to (the next press goes to the one after it).</summary>
    private readonly int?[] lastJumps = new int?[Workforce.Groups];

    /// <summary>
    /// A head count was pressed: goes to the next villager of <paramref name="group"/> and selects
    /// them (only shows them while watching); a note says when the group is empty.
    /// </summary>
    private void JumpToGroup(WorkGroup group)
    {
        System.Collections.Generic.IReadOnlyList<int> members = Workforce.Members(World, ViewTribe, group);
        if (Workforce.Next(members, lastJumps[(int)group]) is not int unit)
        {
            Note(UiText.T("hud.workforce.none." + group.ToString().ToLowerInvariant()));
            return;
        }
        lastJumps[(int)group] = unit;
        if (Spectating)
        {
            Unit shown = World.Unit(unit);
            rig.CentreOnCell(shown.X, shown.Y, glide: true);
            return;
        }
        SelectUnit(unit, centre: true);
    }

    /// <inheritdoc/>
    public void SelectBuilding(int building) => selection.SelectBuilding(building);

    /// <inheritdoc/>
    public void OpenStore(int store, GridPoint cell)
    {
        if (!selection.HasUnits)
        {
            hud.Note(GameTexts.T(PickAVillagerPrompt));
            return;
        }
        hud.Open(new StorePanel(this, store, cell, selection.ToUnitSet()));
    }

    /// <summary>Whether the game is held for a reason besides <paramref name="reason"/>.</summary>
    private bool HeldBesides(HoldReason reason) => holds.Count > (holds.Contains(reason) ? 1 : 0);

    /// <inheritdoc/>
    public void Hold(HoldReason reason, bool on)
    {
        bool changed = on ? holds.Add(reason) : holds.Remove(reason);
        if (changed)
        {
            runner.SetHeld(holds.Count > 0);
        }
    }

    /// <inheritdoc/>
    public void Note(string text) => hud.Note(text);

    /// <inheritdoc/>
    public void Open(HudPopup panel) => hud.Open(panel);

    /// <inheritdoc/>
    public void QuitToMenu() => LeaveTo(UiScreens.MainMenu);

    /// <summary>
    /// Whether the pause button shows paused: alone, the player's own pause (the panels hold the
    /// game besides); with other people, the shared pause, which anyone allowed may lift.
    /// </summary>
    private bool ShownPaused => runner.IsNetwork ? runner.IsPaused : userPaused;

    private void PickSpeed(int percent)
    {
        if (!runner.CanPause && percent == 0)
        {
            return;
        }
        if (percent == 0)
        {
            bool wanted = !ShownPaused;
            if (runner.SetPaused(wanted) == RequestResult.Accepted)
            {
                userPaused = wanted;
            }
            return;
        }
        if (ShownPaused && runner.SetPaused(false) == RequestResult.Accepted)
        {
            userPaused = false;
        }
        if (runner.CanChangeSpeed)
        {
            runner.SetSpeed(percent);
        }
    }

    private void OnCommand(HudCommand command)
    {
        if (SceneBlocksMap() && command != HudCommand.Menu)
        {
            return;
        }
        switch (command)
        {
            case HudCommand.Menu:
                OpenMenu();
                break;
            case HudCommand.Pause:
                PickSpeed(0);
                break;
            case HudCommand.Build when !Spectating:
                if (BuildMenu.Offered(World))
                {
                    hud.Open(new BuildMenu(this));
                }
                else
                {
                    hud.Note(UiText.T("hud.note.no_building"));
                }
                break;
            case HudCommand.Army when !Spectating:
                hud.Open(new ArmyMenu(this));
                break;
            case HudCommand.Roster when !Spectating:
                OpenRoster();
                break;
            case HudCommand.Relations:
                hud.Open(new RelationsPanel(this));
                break;
            case HudCommand.MultiSelect:
                multiSelect = !multiSelect;
                gestures.DragSelects = multiSelect;
                hud.Commands.SetToggled(HudCommand.MultiSelect, multiSelect);
                hud.Note(UiText.T(multiSelect ? "hud.note.multi_on" : "hud.note.multi_off"));
                break;
            case HudCommand.Minimap when !OverviewAvailable:
                ShowNoOverviewHint();
                break;
            case HudCommand.Minimap:
                hud.MinimapShown = !hud.MinimapShown;
                break;
            case HudCommand.Centre:
                CentreOnSelection();
                break;
            case HudCommand.Help:
                hud.Open(new HelpPanel(this));
                break;
            case HudCommand.Objective:
                hud.Open(new ObjectivePanel(this, recentPages));
                break;
        }
    }

    /// <summary>The mission without the overview map (the original's mission 3: the map maker got lost).</summary>
    private const int NoOverviewMission = 3, NoOverviewHint = 20, NoOverviewHintSubject = 1;

    /// <summary>Whether the overview map (the minimap) is there: not in the war mission, as in the original.</summary>
    private bool OverviewAvailable => World.Mission != NoOverviewMission;

    /// <summary>
    /// The original's answer to the overview key in the mission without a map: hint 20, which the
    /// screen shows by itself (it is not the rules'; it holds the game like any hint, alone).
    /// </summary>
    private void ShowNoOverviewHint()
    {
        if (resultsShown || runner.Simulation.CurrentScene is not null)
        {
            return;
        }
        if (runner.IsNetwork)
        {
            hud.Messages.Add(GameTexts.Hint(NoOverviewHint), HintFace, null);
            return;
        }
        hud.Open(new HintDialog(this, new Tribes.Core.Systems.Notices.HintEvent(NoOverviewHint, NoOverviewHintSubject, 0)));
    }

    private void OpenRoster()
    {
        if (Spectating || resultsShown)
        {
            return;
        }
        var roster = new RosterPanel(this);
        runner.RosterOpen = true;
        roster.Closed += () => runner.RosterOpen = false;
        hud.Open(roster);
    }

    /// <summary>Opens the game menu, taking the picture for a save first (without the interface).</summary>
    private void OpenMenu() => OpenMenu(takePicture: true);

    /// <summary>Opens the game menu; <paramref name="takePicture"/>: take the save picture again first.</summary>
    private async void OpenMenu(bool takePicture)
    {
        if (menuOpening || resultsShown || !opened)
        {
            return;
        }
        menuOpening = true;
        Hold(HoldReason.Menu, true);
        if (takePicture)
        {
            try
            {
                // The shooter renders the world without the interface, so the menu needs no hiding.
                Pixels? picture = await shooter.Capture();
                if (!IsInsideTree() || !opened)
                {
                    return;
                }
                thumbnail = picture ?? thumbnail;
            }
            finally
            {
                menuOpening = false;
            }
            if (resultsShown || hud.Popup is not null)
            {
                // Something else opened in the meantime (the result, a hint): no menu over it.
                Hold(HoldReason.Menu, hud.Popup?.Hold == HoldReason.Menu);
                return;
            }
        }
        menuOpening = false;
        bool canSave = !runner.IsNetwork || runner.IsHost;
        var menu = new PauseMenu(this, canSave, canLoad: !runner.IsNetwork);
        menu.Picked += OnMenuChoice;
        menu.SpeedPicked += percent =>
        {
            PickSpeed(percent);
            menu.Refill();
        };
        // The menu holds the game from now on (and lets go when it closes).
        hud.Open(menu);
    }

    private void OnMenuChoice(MenuChoice choice)
    {
        switch (choice)
        {
            case MenuChoice.Resume:
                hud.ClosePopup();
                break;
            case MenuChoice.Save:
                OpenSlots(saving: true);
                break;
            case MenuChoice.Load:
                OpenSlots(saving: false);
                break;
            case MenuChoice.Settings:
                OpenFromMenu(new MatchSettingsPanel(this));
                break;
            case MenuChoice.Help:
                OpenFromMenu(new HelpPanel(this));
                break;
            case MenuChoice.Objective:
                OpenFromMenu(new ObjectivePanel(this, recentPages));
                break;
            case MenuChoice.Forfeit:
                ModalDialog.Show(hud.Dialogs, "hud.menu.forfeit_confirm", (UiKeys.Yes, () =>
                {
                    runner.Forfeit();
                    hud.ClosePopup();
                }), (UiKeys.No, null));
                break;
            case MenuChoice.Quit:
                ModalDialog.Show(hud.Dialogs, runner.IsNetwork && !runner.IsHost ? "hud.menu.quit_confirm_client" : "hud.menu.quit_confirm",
                    (UiKeys.Yes, QuitToMenu), (UiKeys.No, null));
                break;
            case MenuChoice.TestExplore:
                ExploreForTest();
                break;
            case MenuChoice.CheatWin:
                Cheat(VictoryCountdownStart);
                break;
            case MenuChoice.CheatLose:
                Cheat(DefeatCountdownStart);
                break;
        }
    }

    /// <summary>Development: starts the victory or defeat countdown of the rules (debug builds, single player).</summary>
    /// <summary>The menu's test help (playing alone): the land around the camp is explored at once.</summary>
    private void ExploreForTest()
    {
        if (runner.IsNetwork || Spectating || Tribe < 0)
        {
            return;
        }
        hud.ClosePopup();
        runner.Simulation.ExploreForTest(Tribe, TestExploreRadius);
        MatchDev.Log($"MATCH_TEST_EXPLORE tribe={Tribe} radius={TestExploreRadius} tribes={World.Tribes.Count}");
        view.Refresh(World);
    }

    public void Cheat(int countdownStart)
    {
        if (!MatchDev.CheatsAllowed || runner.IsNetwork)
        {
            return;
        }
        hud.ClosePopup();
        if (World.EndCountdown == 0)
        {
            World.EndCountdown = countdownStart;
        }
        MatchDev.Log($"MATCH_CHEAT countdown={countdownStart}");
    }

    private void OpenSlots(bool saving)
    {
        string mode = SaveCatalog.ModeName(request.Mode);
        var slots = new SaveSlotsPanel(this, saving, mode);
        slots.SavePicked += SaveTo;
        slots.LoadPicked += LoadFrom;
        OpenFromMenu(slots);
    }

    /// <summary>Opens a panel picked in the menu; closing it goes back to the menu (the game stays held between).</summary>
    private void OpenFromMenu(HudPopup panel)
    {
        hud.Open(panel);
        panel.Closed += () =>
        {
            Hold(HoldReason.Menu, true);
            CallDeferred(MethodName.BackToMenu);
        };
    }

    private void BackToMenu()
    {
        if (opened && !resultsShown && hud.Popup is null && !ModalDialog.IsOpen(hud.Dialogs) && runner.Simulation.CurrentScene is null)
        {
            OpenMenu(takePicture: false);
            return;
        }
        Hold(HoldReason.Menu, hud.Popup?.Hold == HoldReason.Menu);
    }

    private void SaveTo(string slot)
    {
        // The state is taken now and written in the background; the menu holds the game, so waiting here is not seen.
        SaveSlot? saved = runner.Save(slot);
        if (saved is null || !runner.FlushSaves())
        {
            hud.Note(UiText.T("hud.save.failed"));
            return;
        }
        if (thumbnail is not null)
        {
            runner.AttachPicture(slot, thumbnail);
            runner.FlushSaves();
        }
        hud.Note(UiText.T("hud.save.done"));
        MatchDev.Log($"MATCH_SAVED {saved.Stem}");
    }

    private void LoadFrom(SaveInfo info)
    {
        ModalDialog.Show(hud.Dialogs, "hud.load.confirm", (UiKeys.Yes, () =>
        {
            // The chosen file is copied first: leaving the match writes the autosave, which may be the one chosen.
            const string Loading = "user://saves/.loading.save";
            // A save of this match may still be being written: finish it before reading.
            runner.FlushSaves();
            byte[] data = Godot.FileAccess.GetFileAsBytes(info.DataPath);
            using (Godot.FileAccess? file = data.Length > 0 ? Godot.FileAccess.Open(Loading, Godot.FileAccess.ModeFlags.Write) : null)
            {
                if (file is null)
                {
                    hud.Note(UiText.T("hud.load.failed"));
                    return;
                }
                file.StoreBuffer(data);
            }
            GameSettings settings = GameApp.Instance.Settings;
            GameApp.Instance.Match = new MatchRequest
            {
                Mode = request.Mode,
                Mission = info.Mission,
                Difficulty = info.Difficulty,
                SaveToLoad = ProjectSettings.GlobalizePath(Loading),
                Tutorial = settings.Tutorial,
                ShowHints = settings.ShowHints,
            };
            LeaveTo(UiScreens.Match);
        }), (UiKeys.No, null));
    }
}
