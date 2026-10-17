using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Audio;
using Tribes.Client.MatchPlay.Dev;
using Tribes.Client.MatchPlay.Hud;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Session;
using Tribes.Client.Ui;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Game;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;
using Tribes.Core.Commands;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Notices;
using Tribes.Core.Systems.Story;
using Tribes.Core.Systems.Units;
using Tribes.Core.World;
using Tribes.Session;

namespace Tribes.Client.MatchPlay;

/// <summary>What the rules and the session report, shown on the screen.</summary>
public partial class MatchScreen
{
    private const int ChiefDiedHint = 18, MaxRecentPages = 12, HintFace = 676;
    private const int UnitBornNotice = 1, UnitDiedNotice = 3;

    private bool resultsShown;
    private bool sceneWasShown;

    private SceneSlide slide = SceneSlide.None;

    private void OnTickRan(object? sender, MatchTickEventArgs e)
    {
        pendingTicks.Add(e);
        FollowSlide(e.Result);
    }

    /// <summary>
    /// A scene's first step sliding in, followed as each tick runs (so the hold reaches the very
    /// next tick): a tick held for nothing else, or the one the slide lets the scene move on, is a
    /// frame of the original's story screen. Alone only: with other people nothing holds the game.
    /// </summary>
    private void FollowSlide(TickResult result)
    {
        if (!slide.Done && !HeldBesides(HoldReason.SceneSlide))
        {
            slide.Advance();
        }
        foreach (GameEvent e in result.Events)
        {
            if (e is SceneStepShown shown)
            {
                slide = runner.IsNetwork ? SceneSlide.None : SceneSlide.For(World.Content.Rules.Story.Scenes, shown.Step);
            }
        }
        if (runner.Simulation.CurrentScene is null)
        {
            slide = SceneSlide.None;
        }
        Hold(HoldReason.SceneSlide, slide.Holds);
    }

    /// <summary>Draws and shows every tick that ran this frame, in order.</summary>
    private void ProcessTicks()
    {
        if (pendingTicks.Count == 0)
        {
            UpdateScene();
            return;
        }
        MatchTickEventArgs[] ticks = pendingTicks.ToArray();
        pendingTicks.Clear();
        bool changed = false;
        foreach (MatchTickEventArgs tick in ticks)
        {
            TickResult result = tick.Result;
            if (result.WorldAdvanced)
            {
                view.OnRuleFrame(result.Events);
            }
            else if (AnyApplied(result.Outcomes))
            {
                // Orders carried out while the world stood still (paused): redraw what they changed.
                view.Refresh(World);
            }
            changed |= result.WorldAdvanced || result.Outcomes.Count > 0;
            HandleEvents(result.Events);
            HandleOutcomes(tick);
        }
        FollowViewer();
        if (changed)
        {
            PruneSelection();
            if (hud.MinimapShown)
            {
                hud.Minimap.Refresh(runner.Simulation.Tick);
            }
            if (placing is not null)
            {
                RefreshGhost();
            }
        }
        UpdateScene();
        CheckNoCamp();
        CheckGrace();
        // The open tribes panel looks at the world a few times a second (news refresh it at once).
        if (++relationsLook % RelationsLookEvery == 0)
        {
            RefreshRelations();
        }
        // With other people the runner tells each player's own result (OnMatchOver).
        if (!runner.IsNetwork && runner.Simulation.IsOver && !resultsShown && !World.Script.Active)
        {
            ShowResults(runner.Simulation.Victory == true);
        }
    }

    /// <summary>
    /// A player who joined a running match but got no tribe (the world had no room left for
    /// another camp) is told so once, and watches.
    /// </summary>
    private void CheckNoCamp()
    {
        if (noCampShown || !runner.IsNetwork || runner.Players.ById(runner.LocalPlayerId) is not { HasSlot: true, Tribe: < 0, State: PlayerState.Playing })
        {
            return;
        }
        noCampShown = true;
        MatchDev.Log("MATCH_NO_CAMP");
        ModalDialog.Show(hud.Dialogs, UiText.T("match_room.no_camp"), (UiKeys.Ok, null));
    }

    /// <summary>
    /// The frame from which computer tribes may attack this player's tribe in an endless world
    /// (the rules' opening grace, or a newcomer's own), or null when there is none to tell.
    /// </summary>
    public int? GraceEnd()
    {
        if (World.Endless is null || World.Content.Endless is not { } rules || Spectating || Tribe < 0 || Tribe >= World.Tribes.Count)
        {
            return null;
        }
        return Ui.Logic.EndlessGrace.EndFrame(rules.Pacing.OpeningGraceFrames, rules.Pacing.NewcomerGraceFrames, World.Tribes[Tribe].ArrivedAtFrame);
    }

    /// <summary>At the start (or on joining) an endless world: how long computer tribes leave the tribe alone.</summary>
    private void TellGrace()
    {
        if (GraceEnd() is int end)
        {
            graceTold = true;
            if (World.Frame >= end)
            {
                return;
            }
            graceCheckedFrame = World.Frame;
            hud.Messages.Add(UiText.F("endless.grace.start", Ui.Logic.EndlessGrace.MinutesLeft(World.Frame, end, LocalMatchRunner.TickMilliseconds)), 0, null);
        }
    }

    /// <summary>Tells the grace when it was not told yet, and once when it is over.</summary>
    private void CheckGrace()
    {
        if (!graceTold)
        {
            TellGrace();
        }
        if (graceCheckedFrame < 0 || GraceEnd() is not int end)
        {
            return;
        }
        if (Ui.Logic.EndlessGrace.EndedBetween(graceCheckedFrame, World.Frame, end))
        {
            MatchDev.Log($"MATCH_GRACE_OVER frame={World.Frame}");
            hud.Messages.Add(UiText.T("endless.grace.over"), 0, null);
            hud.Note(UiText.T("endless.grace.over"));
            graceCheckedFrame = -1;
            return;
        }
        graceCheckedFrame = World.Frame;
    }

    private static bool AnyApplied(IReadOnlyList<OrderOutcome> outcomes)
    {
        foreach (OrderOutcome outcome in outcomes)
        {
            if (outcome == OrderOutcome.Applied)
            {
                return true;
            }
        }
        return false;
    }

    private void HandleEvents(IReadOnlyList<GameEvent> events)
    {
        foreach (GameEvent e in events)
        {
            PlayIfSeen(e);
            switch (e)
            {
                case NoticeEvent notice:
                    ShowNotice(notice);
                    break;
                case HintEvent hint:
                    ShowHint(hint);
                    break;
                case SceneStepShown step:
                    RememberPage(step.Step.Page);
                    break;
                case SceneEnded ended when !ended.ResumesPlay:
                    // The campaign's closing story is over: back to the campaign list, every chapter won
                    // (the original goes back to its menus).
                    CallDeferred(MethodName.EndCampaign);
                    break;
                case UnitGone gone:
                    selection.Forget(gone.Unit);
                    break;
                case UnitDeselected deselected:
                    selection.Forget(deselected.Unit);
                    break;
                case BuildingDestroyed destroyed when destroyed.FocusCamera:
                    rig.CentreOnCell(World.Building(destroyed.Building).X, World.Building(destroyed.Building).Y, glide: true);
                    break;
                case MissionEnded ended when !runner.IsNetwork:
                    ShowResults(ended.Victory);
                    break;
                // Tribes there from the start are no news; the ones that come later are.
                case TribeAppeared appeared when World.Tribes[appeared.Tribe].ArrivedAtFrame > 0:
                    ShowNews(Ui.Logic.EndlessNews.TribeAppeared, appeared.Tribe, -1, -1, (appeared.Camp.X, appeared.Camp.Y));
                    break;
                case AllianceInvited invited:
                    ShowNews(Ui.Logic.EndlessNews.Invited, invited.From, invited.To);
                    break;
                case AllianceDeclined declined:
                    ShowNews(Ui.Logic.EndlessNews.Declined, declined.From, declined.To);
                    break;
                case AllianceInvitationLapsed lapsed:
                    ShowNews(Ui.Logic.EndlessNews.Lapsed, lapsed.From, lapsed.To);
                    break;
                case AllianceFormed formed:
                    ShowNews(Ui.Logic.EndlessNews.Formed, formed.A, formed.B);
                    break;
                case AllianceEnded ended:
                    ShowNews(Ui.Logic.EndlessNews.Ended, ended.A, ended.B, ended.By);
                    break;
                case GoodsTransferred gift:
                    ShowNews(Ui.Logic.EndlessNews.Transferred, gift.From, gift.To, -1, null, gift.Good, gift.Amount);
                    break;
                case Tribes.Core.Systems.Match.TribeOut:
                    RefreshRelations();
                    break;
            }
        }
    }

    /// <summary>
    /// Plays the sound of a blow or a throw the player sees: on a cell visible to the viewer and on
    /// the screen now (nothing out of sight is heard, so sounds tell no more than the picture).
    /// </summary>
    private void PlayIfSeen(GameEvent happening)
    {
        if (SoundEffects.Instance is { } sounds && SoundCues.Seen(World, happening) is { } heard
            && Sight.IsVisible(heard.Cell) && OnScreen(heard.Cell))
        {
            sounds.Play(heard.Sound);
        }
    }

    /// <summary>Whether the middle of <paramref name="cell"/> is inside the screen.</summary>
    private bool OnScreen(Tribes.Core.Content.GridPoint cell) => GetViewportRect().HasPoint(rig.MapToScreen(new Vector2(
        (cell.X + 0.5f) * Tribes.Client.World.WorldView.CellWidth, (cell.Y + 0.5f) * Tribes.Client.World.WorldView.CellHeight)));

    private void ShowNotice(NoticeEvent notice)
    {
        // Each player's tribe gets its own notices; watchers of a match with other people get none.
        if (!Ui.Logic.MatchRoom.IsFor(notice.Tribe, Tribe, Spectating && runner.IsNetwork))
        {
            MatchDev.Log($"MATCH_NOTICE kind={notice.Kind} tribe={notice.Tribe} local={Tribe} shown=False");
            return;
        }
        MatchDev.Log($"MATCH_NOTICE kind={notice.Kind} tribe={notice.Tribe} local={Tribe} shown=True");
        SoundEffects.Instance?.Play(SoundCues.ForNotice(notice.Kind));
        bool aboutUnit = notice.Kind is UnitBornNotice or UnitDiedNotice;
        (int, int)? place = notice.X > 0 || notice.Y > 0 ? (notice.X, notice.Y) : null;
        hud.Messages.Add(GameTexts.Notice(World, notice.Kind, aboutUnit ? notice.Value : 0), MessageFeed.FaceFor(notice.Kind), place);
    }

    /// <summary>
    /// An endless world's news in the message list, worded for this player (watchers get the
    /// public news only), and the tribes panel brought up to date.
    /// </summary>
    private void ShowNews(Ui.Logic.EndlessNews news, int a, int b, int by = -1, (int, int)? place = null,
        TribeCounter good = TribeCounter.Food, int amount = 0)
    {
        RefreshRelations();
        // Nothing arrived (the ally's store was full): no news.
        if (news == Ui.Logic.EndlessNews.Transferred && amount <= 0)
        {
            return;
        }
        string? text = AllianceTexts.Describe(this, news, a, b, by, Spectating ? -1 : Tribe, good, amount);
        MatchDev.Log($"MATCH_NEWS {news} a={a} b={b} by={by} local={Tribe} shown={text is not null}");
        if (text is not null)
        {
            hud.Messages.Add(text, 0, place);
        }
    }

    /// <summary>
    /// Brings the open tribes panel up to date (rebuilt only when what it shows changed), and closes
    /// a gift to a tribe that is no longer an ally.
    /// </summary>
    private void RefreshRelations()
    {
        switch (hud.Popup)
        {
            case RelationsPanel panel:
                panel.FollowWorld();
                break;
            case GiftPanel gift when !gift.StillPossible():
                gift.Close();
                break;
        }
    }

    private void ShowHint(HintEvent hint)
    {
        // A hint is for the player of its tribe only.
        if (!Ui.Logic.MatchRoom.IsFor(hint.Tribe, Tribe, Spectating) || resultsShown)
        {
            MatchDev.Log($"MATCH_HINT hint={hint.Hint} tribe={hint.Tribe} local={Tribe} shown=False");
            return;
        }
        MatchDev.Log($"MATCH_HINT hint={hint.Hint} tribe={hint.Tribe} local={Tribe} shown=True");
        SoundEffects.Instance?.Play(SoundCues.Hint);
        if (runner.IsNetwork)
        {
            // Nobody pauses a match with other people: the hint goes to the message list instead.
            hud.Messages.Add(GameTexts.Hint(hint.Hint), HintFace, null);
            if (hint.Hint == ChiefDiedHint)
            {
                CallDeferred(MethodName.OpenRoster);
            }
            return;
        }
        var dialog = new HintDialog(this, hint);
        dialog.ShowPlace += (x, y) => rig.CentreOnCell(x, y, glide: true);
        if (hint.Hint == ChiefDiedHint)
        {
            dialog.Closed += () => CallDeferred(MethodName.OpenRoster);
        }
        hud.Open(dialog);
    }

    private void RememberPage(int page)
    {
        if (recentPages.Count > 0 && recentPages[^1] == page)
        {
            return;
        }
        recentPages.Add(page);
        if (recentPages.Count > MaxRecentPages)
        {
            recentPages.RemoveAt(0);
        }
    }

    private void HandleOutcomes(MatchTickEventArgs tick)
    {
        IReadOnlyList<OrderOutcome> outcomes = tick.Result.Outcomes;
        for (int i = 0; i < tick.LocalOrders.Count && i < outcomes.Count; i++)
        {
            if (tick.LocalOrders[i] is { } order && outcomes[i] != OrderOutcome.Applied)
            {
                hud.Note(UiText.T(OutcomeTexts.Key(outcomes[i], buildingLimit: World.BuildingLimit is not null)));
                MatchDev.Log($"ORDER_REFUSED {order.GetType().Name} {outcomes[i]}");
            }
            else if (tick.LocalOrders[i] is { } applied)
            {
                MatchDev.Log($"ORDER_APPLIED {applied.GetType().Name}");
            }
        }
    }

    /// <summary>Shows the story scene's step, or the game again when the scene is over.</summary>
    private void UpdateScene()
    {
        SceneStep? step = runner.Simulation.CurrentScene;
        bool shown = step is not null;
        hud.Scene.Show(step, runner.Simulation.CanContinueScene && slide.Done);
        hud.Scene.ShowSlide(!slide.Done, slide.Closed);
        if (shown != sceneWasShown)
        {
            sceneWasShown = shown;
            hud.SetSceneMode(shown);
            if (shown)
            {
                CancelMode();
                hud.ClosePopup();
            }
        }
        if (step is { ChapterScreen: false, FocusUnit: > 0 } && step.FocusUnit <= World.UnitSlots && step != followedStep)
        {
            followedStep = step;
            Unit focus = World.Unit(step.FocusUnit);
            rig.CentreOnCell(focus.X, focus.Y, glide: true);
        }
        if (shown && MatchDev.SkipScenes && runner.Simulation.CanContinueScene)
        {
            ContinueScene();
        }
    }

    private SceneStep? followedStep;

    /// <summary>A tap on the scene: go on when the rules take it.</summary>
    private void ContinueScene()
    {
        // One tap per tick: a second one would reach the rules after the first already went on.
        long tick = runner.Simulation.Tick;
        // Not while the step slides in: the original takes no tap then.
        if (runner.Simulation.CanContinueScene && slide.Done && !Spectating && tick != continueSentAt)
        {
            continueSentAt = tick;
            Send(new AdvanceScene(Tribe));
        }
    }

    private long continueSentAt = -1;

    private void OnPlayersChanged(object? sender, EventArgs e)
    {
        ApplyColours();
        FollowViewer();
        hud.Players.Refresh();
        hud.Banner.Text = SessionText.Role(runner.Role);
        hud.Banner.Visible = hud.Banner.Text.Length > 0;
        hud.Commands.Arrange(metrics, vertical: !metrics.Portrait);
    }

    private void OnNotice(object? sender, MatchNoticeEventArgs e)
    {
        if (e.Notice.Kind == MatchNoticeKind.Autosaved)
        {
            _ = SaveAutosavePictureAsync();
        }
        string text = SessionText.Describe(e.Notice);
        if (text.Length > 0)
        {
            hud.Messages.Add(text, 0, null);
        }
    }

    private void OnMatchOver(object? sender, MatchOverEventArgs e)
    {
        if (runner.IsNetwork)
        {
            ShowResults(e.LocalVictory);
        }
    }

    private void OnStopped(object? sender, MatchStoppedEventArgs e)
    {
        if (e.Stop.Reason == StopReason.Quit)
        {
            UiScreens.Go(UiScreens.MainMenu);
            return;
        }
        ModalDialog.Show(hud.Dialogs, SessionText.Describe(e.Stop), (UiKeys.Ok, () => UiScreens.Go(UiScreens.MainMenu)));
    }

    private void OnSimulationReplaced(object? sender, EventArgs e)
    {
        // Ticks of the world that was replaced must not be drawn on the new one.
        pendingTicks.Clear();
        selection.Clear();
        ShowWorld();
    }

    /// <summary>The result screen (once per match).</summary>
    private void ShowResults(bool victory)
    {
        if (resultsShown)
        {
            return;
        }
        resultsShown = true;
        CancelMode();
        SoundEffects.Instance?.Play(SoundCues.ForResult(victory));
        bool campaign = request.Mode == MatchMode.Campaign && !runner.IsNetwork;
        if (campaign)
        {
            KeepCampaignProgress(victory);
        }
        IReadOnlyList<Ui.Logic.CampaignChapter> chapters = MatchConnection.Chapters();
        int? next = campaign && victory ? CampaignFlow.NextMission(chapters, World.Mission) : null;
        bool ending = campaign && victory && CampaignFlow.EndsCampaign(chapters, World.Mission) && runner.Simulation.ClosingSceneFollows;
        var panel = new ResultsPanel(this, victory, campaign, next is not null, ending);
        panel.Chosen += choice => OnResultChosen(choice);
        hud.Open(panel);
        MatchDev.Log($"MATCH_RESULT victory={victory} score={World.Tribes[Math.Max(0, Tribe)][TribeCounter.Score]}");
    }

    private void KeepCampaignProgress(bool victory)
    {
        CampaignProgress progress = CampaignProgress.Load();
        progress.TutorialFlags = World.Script.TutorialFlags & ~1;
        progress.Save();
        if (victory)
        {
            foreach (int mission in CampaignFlow.CompletedBy(MatchConnection.Chapters(), World.Mission))
            {
                CampaignProgress.MarkCompleted(mission);
            }
        }
    }

    private void OnResultChosen(ResultChoice choice)
    {
        switch (choice)
        {
            case ResultChoice.Next:
                if (CampaignFlow.NextMission(MatchConnection.Chapters(), World.Mission) is int next)
                {
                    GameApp.Instance.Match = request with { Mission = next, SaveToLoad = null };
                    LeaveTo(UiScreens.Match);
                }
                break;
            case ResultChoice.Continue:
                LeaveTo(UiScreens.Campaign);
                break;
            case ResultChoice.Ending:
                PlayEnding();
                break;
            case ResultChoice.Retry:
                GameApp.Instance.Match = request with { Mission = CampaignFlow.StartMissionFor(MatchConnection.Chapters(), World.Mission), SaveToLoad = null };
                LeaveTo(UiScreens.Match);
                break;
            default:
                LeaveTo(UiScreens.MainMenu);
                break;
        }
    }

    /// <summary>
    /// The picture of a timed autosave, taken a frame or two after it was written (the game goes
    /// on meanwhile): the one taken when the menu opened while the menu is up, else the world as
    /// the screen shows it now. Shrinking, encoding and writing happen in the background.
    /// </summary>
    private async Task SaveAutosavePictureAsync(bool dryRun = false)
    {
        if (!opened || (runner.IsNetwork && !runner.IsHost) || runner.Phase is MatchPhase.Over or MatchPhase.Stopped)
        {
            // No autosave is written then: keep the picture of the one there is.
            return;
        }
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Pixels? picture = hud.Popup?.Hold == HoldReason.Menu && thumbnail is not null ? thumbnail : await shooter.Capture();
        if (picture is not null && opened)
        {
            // Written after the autosave and only while it is the newest save of the slot (a dry
            // run at the start has no autosave yet, so nothing is written).
            runner.AttachPicture(dryRun ? DryRunSlot : SaveSlot.Auto, picture);
        }
        MatchDev.Log($"MATCH_AUTOSAVE_PICTURE readback={shooter.LastReadbackMs:0.00}ms ({picture?.Data.Length ?? 0} bytes) after {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:0}ms (two frames); shrink, encode and write in the background");
    }

    /// <summary>A slot no save of a match uses, so a dry-run picture is never written.</summary>
    private const string DryRunSlot = "5";

    /// <summary>
    /// The first autosave picture of a run is slow (render target set up, shaders, code compiled on
    /// first use, most on Android): take one once the match has settled, and throw it away.
    /// </summary>
    private async Task WarmUpPictureAsync()
    {
        await shooter.WaitFrames(30);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        await SaveAutosavePictureAsync(dryRun: true);
        MatchDev.Log($"SESSION_PICTURE_WARMUP readback={shooter.LastReadbackMs:0.00}ms after {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}ms");
    }

    /// <summary>
    /// Leaves the match: the picture is taken first (no frame is left to render one, so it is the
    /// menu picture when the menu was open, else cut from the screen as it is), then the runner
    /// quits and writes the leaving autosave, and the picture is written after it.
    /// </summary>
    private void QuitWithPicture()
    {
        Pixels? picture = null;
        if (runner.IsHost && runner.Phase is not (MatchPhase.Over or MatchPhase.Stopped))
        {
            try
            {
                picture = hud.Popup?.Hold == HoldReason.Menu && thumbnail is not null
                    ? thumbnail
                    : ThumbnailShooter.FromScreenshot(GetViewport().GetTexture().GetImage());
            }
            catch (ObjectDisposedException e)
            {
                GD.PushWarning($"The autosave picture was not kept: {e.Message}");
            }
        }
        runner.Quit();
        if (picture is not null)
        {
            runner.AttachPicture(SaveSlot.Auto, picture);
        }
        runner.FlushSaves();
    }

    /// <summary>
    /// The campaign is won: its closing story plays in this world, as the original goes from the
    /// result of its last mission (the result panel closes; the story shows as any other).
    /// </summary>
    private void PlayEnding()
    {
        if (!runner.Simulation.PlayClosingScene())
        {
            EndCampaign();
            return;
        }
        hud.ClosePopup();
        MatchDev.Log("MATCH_ENDING closing story");
    }

    /// <summary>The closing story is over: the campaign list shows every chapter won.</summary>
    private void EndCampaign()
    {
        MatchDev.Log("MATCH_CAMPAIGN_COMPLETE");
        LeaveTo(UiScreens.Campaign);
    }

    /// <summary>Ends the match (the runner saves where it does) and shows <paramref name="scene"/>.</summary>
    private void LeaveTo(string scene)
    {
        if (opened)
        {
            UnhookRunner();
            try
            {
                QuitWithPicture();
            }
            finally
            {
                // The match always ends here, picture or not.
                opened = false;
                runner.Dispose();
            }
        }
        UiScreens.Go(scene);
    }
}
