using System;
using System.Collections.Generic;
using Godot;
using Tribes.Client.App;
using Tribes.Client.Audio;
using Tribes.Client.Input;
using Tribes.Client.Map;
using Tribes.Client.MatchPlay.Dev;
using Tribes.Client.MatchPlay.Hud;
using Tribes.Client.MatchPlay.Logic;
using Tribes.Client.Session;
using Tribes.Client.Ui;
using Tribes.Client.Ui.Art;
using Tribes.Client.Ui.Dev;
using Tribes.Client.Ui.Text;
using Tribes.Client.Ui.Widgets;
using Tribes.Client.World;
using Tribes.Client.World.Scene;
using Tribes.Core.World;
using Tribes.Session;

namespace Tribes.Client.MatchPlay;

/// <summary>
/// The match screen (scenes/match/match.tscn): the world view under a camera, the interface over
/// it, and the match runner of the session layer underneath. It opens the match the menus asked
/// for (<see cref="GameApp.Match"/>), runs it every frame at the rules' own pace, draws each rule
/// frame, turns taps, clicks and keys into orders for this device's tribe (through the runner's
/// order sink, never applied here) and shows what the rules report.
/// </summary>
public partial class MatchScreen : Node2D, IMatchActions
{
    private const float CombatMoodFrames = 50;

    private readonly UnitSelection selection = new();
    private readonly HashSet<HoldReason> holds = new();
    private readonly List<MatchTickEventArgs> pendingTicks = new();
    private bool noCampShown;
    private bool cameraAtOwnCamp;
    private int graceCheckedFrame = -1;
    private bool graceTold;
    private int relationsLook;

    /// <summary>Screen updates between two looks of the open tribes panel at the world.</summary>
    private const int RelationsLookEvery = 6;
    private readonly List<int> recentPages = new();
    private MatchRequest request = null!;
    private IMatchRunner runner = null!;
    private WorldView view = null!;
    private MapCamera camera = null!;
    private ThumbnailShooter shooter = null!;
    private CameraRig rig = null!;
    private PointerGestures gestures = null!;
    private WorldOverlay overlay = null!;
    private MatchHud hud = null!;
    private HudMetrics metrics = new();
    private double playSeconds;
    private bool userPaused;
    private bool opened;
    private int shownSelectionVersion = -1;
    private int lastCentreGroup = -1;
    private double lastCentreGroupTime;

    /// <summary>The match screen shown now, if any.</summary>
    public static MatchScreen? Current { get; private set; }

    /// <inheritdoc/>
    public IMatchRunner Runner => runner;

    /// <inheritdoc/>
    public MatchMode Mode => request.Mode;

    /// <inheritdoc/>
    public GameWorld World => runner.Simulation.World;

    /// <inheritdoc/>
    public int Tribe => runner.LocalTribe;

    /// <inheritdoc/>
    public int ViewTribe => Math.Clamp(runner.ViewTribe, 0, World.Tribes.Count - 1);

    /// <inheritdoc/>
    public ViewerSight Sight => view.Sight ?? ViewerSight.Of(World, ViewTribe);

    /// <inheritdoc/>
    public bool Spectating => runner.LocalTribe < 0 || !runner.Orders.CanSubmit;

    /// <inheritdoc/>
    public UnitSelection Selection => selection;

    /// <inheritdoc/>
    public HudMetrics Metrics => metrics;

    /// <inheritdoc/>
    public Control DialogLayer => hud.Dialogs;

    /// <summary>The interface (for scripted checks).</summary>
    public MatchHud Hud => hud;

    /// <summary>The world view (for checks).</summary>
    public WorldView View => view;

    /// <summary>The camera rig (for checks).</summary>
    public CameraRig CameraRig => rig;

    /// <summary>The overlay of ghosts and marks (for checks).</summary>
    public WorldOverlay Overlay => overlay;

    /// <summary>Seconds played in this match (not counting pauses).</summary>
    public double PlaySeconds => playSeconds;

    /// <summary>Whether the match is running (opened without error).</summary>
    public bool IsOpen => opened;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Current = this;
        GetTree().Root.Theme = PixelTheme.Current;
        GetTree().QuitOnGoBack = false;
        TextureFilter = TextureFilterEnum.Nearest;
        request = GameApp.Instance.Match ?? DevRequest();
        BuildScene();
        try
        {
            runner = MatchConnection.Open(request);
        }
        catch (Exception e) when (e is SaveFileException or ArgumentException or System.IO.IOException or InvalidOperationException)
        {
            string text = e is SaveFileException save ? SessionText.SaveProblem(save) : e.Message;
            GD.PushWarning($"match could not open: {e.Message}");
            ShowFailure(text);
            return;
        }
        opened = true;
        hud = new MatchHud(this);
        AddChild(hud);
        WireHud();
        HookRunner();
        if (DevOptions.Has("session-save-trace"))
        {
            AddChild(new Tribes.Client.Session.Dev.SaveFrameTrace(runner));
        }
        ShowWorld();
        PlaceCameraAtStart();
        if (runner.Phase == MatchPhase.WaitingForPlayers)
        {
            hud.Dialogs.AddChild(new ResumeWaitPanel(runner));
        }
        UiText.Changed += OnLanguageChanged;
        MusicPlayer.Instance?.PlayMood(MusicPlayer.Normal);
        if (DevOptions.GetInt("match-speed", 0) is int speed && speed > 0)
        {
            runner.SetSpeed(speed);
        }
        if (DevOptions.Has("site-check") && MatchDev.CheatsAllowed && !runner.IsNetwork)
        {
            SiteCheck.Start(this);
        }
        MatchDev.Log($"MATCH_READY mode={request.Mode} mission={World.Mission} tribe={Tribe} view={ViewTribe} network={runner.IsNetwork} touch={metrics.Touch}");
        TellGrace();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        UiText.Changed -= OnLanguageChanged;
        if (Current == this)
        {
            Current = null;
        }
        if (opened)
        {
            // Connected only once the match opened (a match that failed to open never was).
            GetTree().Root.SizeChanged -= OnWindowResized;
            // Closing the window still saves (the runner's autosave): keep its picture with it.
            UnhookRunner();
            try
            {
                QuitWithPicture();
            }
            finally
            {
                opened = false;
                runner.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (!opened)
        {
            return;
        }
        if (metricsStale)
        {
            UpdateMetrics();
        }
        view.RuleFrameSeconds = (float)Math.Clamp(runner.TickSeconds, 0.02, 1);
        // A question on screen holds the game (alone; with other people the session decides).
        Hold(HoldReason.Question, ModalDialog.IsOpen(hud.Dialogs));
        runner.Update(delta);
        if (!runner.IsPaused && runner.Phase == MatchPhase.Running)
        {
            playSeconds += delta;
        }
        ProcessTicks();
        // The setting may change in the menu during the match.
        rig.EdgeScroll = GameApp.Instance.Settings.EdgeScroll && !metrics.Touch;
        rig.Process(delta, KeyPan(), EdgeScrollMouse(), GetViewportRect().Size);
        ScrollWhileDragging(delta);
        Rect2 seen = rig.VisibleMap();
        if (seen != hud.Minimap.ViewArea)
        {
            hud.Minimap.ViewArea = seen;
            hud.Minimap.QueueRedraw();
        }
        RefreshHud();
        UpdateMarks();
        UpdateMusic();
        LogFrameRate(delta);
    }

    private double fpsClock;
    private int fpsFrames;
    private double fpsWorst;

    /// <summary>"--match-fps-log": every two seconds MATCH_FPS with the frame rate, the slowest frame and the drawing work.</summary>
    private void LogFrameRate(double delta)
    {
        if (!DevOptions.Has("match-fps-log"))
        {
            return;
        }
        fpsClock += delta;
        fpsFrames++;
        fpsWorst = Math.Max(fpsWorst, delta);
        if (fpsClock < 2)
        {
            return;
        }
        GD.Print($"MATCH_FPS fps={fpsFrames / fpsClock:0.0} worst={fpsWorst * 1000:0}ms engine={Engine.GetFramesPerSecond():0} tick={runner.Simulation.Tick} "
            + $"units={BuildingOffers.LivingUnits(World, ViewTribe).Count} items={view.SceneItems.Count} compose={view.ComposeMilliseconds:0.0}ms "
            + $"window={DisplayServer.WindowGetSize()} scale={GetTree().Root.ContentScaleFactor} zoom={rig.Zoom:0.00} touch={metrics.Touch} button={metrics.Button}");
        fpsClock = 0;
        fpsFrames = 0;
        fpsWorst = 0;
    }

    private void BuildScene()
    {
        view = new WorldView { Name = "World", SmoothMotion = true, WholeBuildings = true };
        AddChild(view);
        overlay = new WorldOverlay { Name = "Overlay" };
        AddChild(overlay);
        camera = new MapCamera { Name = "Camera" };
        AddChild(camera);
        shooter = new ThumbnailShooter(GetViewport());
        AddChild(shooter);
        _ = WarmUpPictureAsync();
        camera.MakeCurrent();
        rig = new CameraRig(camera, view);
        gestures = new PointerGestures { Name = "Gestures", SecondaryOnRelease = true };
        AddChild(gestures);
    }

    private void WireHud()
    {
        metrics = HudMetrics.Current(GetViewport());
        rig.SetPixelScale((int)GetTree().Root.ContentScaleFactor);
        rig.EdgeScroll = GameApp.Instance.Settings.EdgeScroll && !metrics.Touch;
        hud.LaidOut += () =>
        {
            rig.Insets = hud.Insets;
            rig.Clamp();
            RevealSelected();
        };
        hud.Top.MenuPressed += OpenMenu;
        hud.Top.SpeedPicked += PickSpeed;
        hud.Top.ChiefPressed += OpenRoster;
        hud.Workers.GroupPressed += JumpToGroup;
        hud.Commands.Pressed += OnCommand;
        hud.Messages.PlaceTapped += (x, y) => rig.CentreOnCell(x, y, glide: true);
        hud.Minimap.Pointed += point => rig.CentreOn(point);
        hud.Scene.ContinueRequested += ContinueScene;
        hud.Placement.Confirmed += ConfirmMode;
        hud.Placement.Cancelled += CancelMode;
        view.ViewResized += () => CallDeferred(MethodName.OnViewResized);
        GetTree().Root.SizeChanged += OnWindowResized;
        WireGestures();
    }

    /// <summary>Whether the interface sizes must be worked out again (the window changed); asking the display every frame is slow on phones.</summary>
    private bool metricsStale = true;

    private void OnWindowResized() => metricsStale = true;

    private void OnViewResized()
    {
        metricsStale = true;
        UpdateMetrics(force: true);
        rig.Clamp();
    }

    /// <summary>Recomputes the interface sizes when the window or its pixel scale changed.</summary>
    private void UpdateMetrics(bool force = false)
    {
        metricsStale = false;
        HudMetrics now = HudMetrics.Current(GetViewport());
        if (!force && now == metrics)
        {
            return;
        }
        bool sizes = now.Button != metrics.Button || now.Touch != metrics.Touch;
        metrics = now;
        rig.SetPixelScale((int)GetTree().Root.ContentScaleFactor);
        if (sizes)
        {
            hud.Context.Refresh(force: true);
            hud.Players.Refresh();
        }
        hud.Relayout();
    }

    private void HookRunner()
    {
        runner.TickRan += OnTickRan;
        runner.PlayersChanged += OnPlayersChanged;
        runner.Notice += OnNotice;
        runner.MatchOver += OnMatchOver;
        runner.Stopped += OnStopped;
        runner.SimulationReplaced += OnSimulationReplaced;
    }

    private void UnhookRunner()
    {
        runner.TickRan -= OnTickRan;
        runner.PlayersChanged -= OnPlayersChanged;
        runner.Notice -= OnNotice;
        runner.MatchOver -= OnMatchOver;
        runner.Stopped -= OnStopped;
        runner.SimulationReplaced -= OnSimulationReplaced;
    }

    private void ShowWorld()
    {
        view.Show(World, WantedViewpoint());
        ApplyColours();
        view.Refresh(World);
        hud.Minimap.Refresh(runner.Simulation.Tick, force: true);
        if (!OverviewAvailable)
        {
            hud.MinimapShown = false;
        }
        hud.Players.Refresh();
        hud.Banner.Text = SessionText.Role(runner.Role);
        hud.Banner.Visible = hud.Banner.Text.Length > 0;
    }

    /// <summary>The tribe colours from the player table: chosen colours first, the rest get free ones.</summary>
    private void ApplyColours()
    {
        int tribes = World.Tribes.Count;
        var colours = new int[tribes];
        Array.Fill(colours, -1);
        var used = new HashSet<int>();
        foreach (PlayerEntry player in runner.Players)
        {
            if (player.Tribe >= 0 && player.Tribe < tribes && player.Colour >= 0 && player.Colour < TribeColours.Count && used.Add(player.Colour))
            {
                colours[player.Tribe] = player.Colour;
            }
        }
        for (int t = 0; t < tribes; t++)
        {
            if (colours[t] >= 0)
            {
                continue;
            }
            int preferred = TribeColours.DefaultFor(t);
            // Past eight tribes every colour is used: the rest keep their own, shared with another tribe.
            int colour = used.Contains(preferred) && used.Count < TribeColours.Count ? FirstFree(used) : preferred;
            colours[t] = colour;
            used.Add(colour);
        }
        view.SetTribeColours(colours);
    }

    private static int FirstFree(HashSet<int> used)
    {
        for (int c = 0; c < TribeColours.Count; c++)
        {
            if (!used.Contains(c))
            {
                return c;
            }
        }
        return 0;
    }

    /// <inheritdoc/>
    public int ColourOf(int tribe) => tribe >= 0 && tribe < World.Tribes.Count ? view.TribeColour(tribe) : 0;

    private void PlaceCameraAtStart()
    {
        float zoom = DefaultZoom();
        camera.Zoom = new Vector2(zoom, zoom);
        hud.Relayout();
        rig.Insets = hud.Insets;
        (int x, int y) = HomeCell(ViewTribe);
        rig.CentreOnCell(x, y);
        cameraAtOwnCamp = Tribe >= 0;
    }

    /// <summary>
    /// Keeps the view on what this device may see: a player sees through their tribe (with its
    /// allies' sight), anyone else (watching, out, gave up, no camp) through every human player's.
    /// A player who joins a running match gets the world before the tick that gives them a tribe,
    /// so the view first shows the first human tribe; once the new tribe is in the world the view
    /// (fog, explored land, the camera's limits, the minimap) switches to it, and the camera moves
    /// to its camp once.
    /// </summary>
    private void FollowViewer()
    {
        Viewpoint wanted = WantedViewpoint();
        if (wanted.Tribe < 0 || wanted.Tribe >= World.Tribes.Count)
        {
            return;
        }
        if (wanted != view.Viewpoint)
        {
            view.SetViewpoint(wanted);
            if (hud.MinimapShown)
            {
                hud.Minimap.Refresh(runner.Simulation.Tick, force: true);
            }
            rig.Clamp();
            MatchDev.Log($"MATCH_VIEWER tribe={wanted.Tribe} spectating={wanted.Spectating}");
        }
        if (!cameraAtOwnCamp && Tribe >= 0 && HasCamp(Tribe))
        {
            cameraAtOwnCamp = true;
            (int x, int y) = HomeCell(Tribe);
            rig.CentreOnCell(x, y);
            MatchDev.Log($"MATCH_CAMERA_HOME tribe={Tribe} cell={x},{y}");
        }
    }

    /// <summary>The player's own view while playing a tribe, else a watcher's, centred on <see cref="ViewTribe"/>.</summary>
    private Viewpoint WantedViewpoint() =>
        runner.Role == LocalRole.Player && runner.LocalTribe >= 0 && runner.LocalTribe < World.Tribes.Count
            ? Viewpoint.Player(runner.LocalTribe)
            : Viewpoint.Spectator(ViewTribe);

    /// <summary>Whether <paramref name="tribe"/> has anything on the map to bring the camera to.</summary>
    private bool HasCamp(int tribe) =>
        BuildingOffers.StandingBuildings(World, tribe).Count > 0 || BuildingOffers.LivingUnits(World, tribe).Count > 0;

    /// <summary>A zoom at which a map cell is about 7 mm wide on a touch screen, else 1.</summary>
    private float DefaultZoom()
    {
        if (DevOptions.Get("zoom") is { } text && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float forced))
        {
            return Mathf.Clamp(forced, CameraRig.MinZoom, CameraRig.MaxZoom);
        }
        if (!metrics.Touch)
        {
            return 1;
        }
        float wanted = 7 * metrics.PixelsPerMillimetre / WorldView.CellWidth;
        var zooms = CameraLimits.PixelPerfectZooms((int)GetTree().Root.ContentScaleFactor, CameraRig.MinZoom, CameraRig.MaxZoom);
        return CameraLimits.Step(zooms, Mathf.Clamp(wanted, 1, 3), 0);
    }

    /// <summary>Where the view of <paramref name="tribe"/> starts: its chief, else its first building, else its first unit, else the map's middle.</summary>
    internal (int X, int Y) HomeCell(int tribe)
    {
        GameWorld world = World;
        int chief = world.Tribes[tribe][TribeCounter.Chief];
        if (chief >= 1 && chief <= world.UnitSlots && world.Unit(chief).HitPoints > 0 && world.Unit(chief).Tribe == tribe)
        {
            return (world.Unit(chief).X, world.Unit(chief).Y);
        }
        foreach (int id in BuildingOffers.StandingBuildings(world, tribe))
        {
            return (world.Building(id).X + 1, world.Building(id).Y + 1);
        }
        foreach (int id in BuildingOffers.LivingUnits(world, tribe))
        {
            return (world.Unit(id).X, world.Unit(id).Y);
        }
        return (world.Map.PlayableSize.Width / 2, world.Map.PlayableSize.Height / 2);
    }

    /// <summary>
    /// Frames since the fighting this screen's music follows: the viewed tribe's own (in the
    /// campaign the world's); a spectator hears any human-ruled tribe's.
    /// </summary>
    private int FramesSinceCombatSeen()
    {
        if (!Spectating || World.Mode != WorldMode.Match)
        {
            return TribeRoles.FramesSinceCombat(World, ViewTribe);
        }
        int least = TribeState.QuietAfterFrames;
        foreach (TribeState tribe in World.Tribes)
        {
            if (tribe.Controller.IsHumanRuled)
            {
                least = Math.Min(least, tribe.FramesSinceCombat);
            }
        }
        return least;
    }

    private void UpdateMusic()
    {
        if (MusicPlayer.Instance is not { } music || World.Script.Active)
        {
            return;
        }
        string mood = FramesSinceCombatSeen() < CombatMoodFrames ? MusicPlayer.Combat
            : TribeRoles.Army(World, ViewTribe) is { AllOut: true } or { Rally: true } ? MusicPlayer.Tension : MusicPlayer.Normal;
        if (music.Mood != mood)
        {
            music.PlayMood(mood);
        }
    }

    private void OnLanguageChanged()
    {
        hud.Context.Refresh(force: true);
        hud.Players.Refresh();
        hud.RefreshCommands();
    }

    private const int DevEndlessStartArea = 128;

    /// <summary>
    /// A request straight from the command line (development): campaign mission or custom map
    /// "--match-mission=N", or an endless world "--match-endless=SEED" with "--match-computers=N"
    /// computer tribes.
    /// </summary>
    private static MatchRequest DevRequest()
    {
        int mission = Math.Max(0, DevOptions.GetInt("match-mission", 1));
        int difficulty = Math.Clamp(DevOptions.GetInt("match-difficulty", 0), 0, 1);
        GameSettings settings = GameApp.Instance.Settings;
        if (DevOptions.GetInt("match-endless", 0) is int seed && seed > 0)
        {
            return new MatchRequest
            {
                Mode = MatchMode.Endless,
                Difficulty = difficulty,
                ShowHints = false,
                Endless = new EndlessSettings(seed, DevEndlessStartArea, Math.Max(0, DevOptions.GetInt("match-computers", 1))),
                Players = [new MatchPlayer(0, settings.PlayerName, false, -1, 0, 0, true)],
            };
        }
        return new MatchRequest
        {
            // The missions from the first custom map on are custom maps, not campaign missions.
            Mode = mission >= Tribes.Core.Systems.Missions.MissionStart.FirstCustomMission ? MatchMode.Custom : MatchMode.Campaign,
            Mission = mission,
            Difficulty = difficulty,
            Tutorial = settings.Tutorial,
            ShowHints = settings.ShowHints,
            Players =
            [
                new MatchPlayer(0, settings.PlayerName, false, -1, 0, 0, true),
                new MatchPlayer(1, UiText.T("player.computer"), true, -1, 1, difficulty, false),
            ],
        };
    }

    private void ShowFailure(string text)
    {
        var layer = new CanvasLayer { Layer = 20 };
        AddChild(layer);
        var root = new Control { Theme = PixelTheme.Current, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        ModalDialog.Show(root, text, (UiKeys.Ok, () => UiScreens.Go(UiScreens.MainMenu)));
    }
}
