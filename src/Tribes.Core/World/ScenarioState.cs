using System;
using System.Collections.Generic;
using Tribes.Core.Content;

namespace Tribes.Core.World;

/// <summary>Story scenes, mission objectives and the tutorial.</summary>
public sealed class ScriptState
{
    /// <summary>Actor slots a scene uses.</summary>
    public const int ActorSlots = 40;

    /// <summary>Seats of the council scene.</summary>
    public const int MeetingSeats = 4;

    private readonly int[] actors = new int[ActorSlots];
    private int sceneId, stepCount, step, focusUnit, sceneKind, tutorialFlags, objectiveStage;
    private int tutorialObjective, tutorialTimer, unlocksFired, behaviourMask, meetingCount, chiefWasInside;

    internal ScriptState()
    {
        for (int i = 0; i < MeetingSeats; i++)
        {
            Seats[i] = new MeetingSeat();
        }
    }

    /// <summary>Unit slot of actor <paramref name="i"/>.</summary>
    public int Actor(int i) => actors[i];

    /// <summary>Sets actor <paramref name="i"/>.</summary>
    public void SetActor(int i, int unit) => actors[i] = Wrap.Signed8(unit);

    /// <summary>Scene being played or last played.</summary>
    public int SceneId { get => sceneId; set => sceneId = Wrap.Signed8(value); }

    /// <summary>Steps of the scene.</summary>
    public int StepCount { get => stepCount; set => stepCount = Wrap.Signed8(value); }

    /// <summary>Step shown.</summary>
    public int Step { get => step; set => step = Wrap.Signed8(value); }

    /// <summary>Unit the step is about.</summary>
    public int FocusUnit { get => focusUnit; set => focusUnit = Wrap.Signed8(value); }

    /// <summary>Layout kind of the scene.</summary>
    public int SceneKind { get => sceneKind; set => sceneKind = Wrap.Signed8(value); }

    /// <summary>Where the scene's steps start in the scene table.</summary>
    public int TableBase { get; set; }

    /// <summary>Whether a scene is playing.</summary>
    public bool Active { get; set; }

    /// <summary>
    /// World frames the current scene step has run: 0 not started, -1 none to run (no timer, or
    /// all run). The player may go on to the next step only while it is 0 or less.
    /// </summary>
    public int StepFrame { get; set; }

    /// <summary>
    /// Whether the scene is shown on the chapter screen (a picture between missions): its actors are
    /// not staged and the world does not advance.
    /// </summary>
    public bool ChapterScreen { get; set; }

    /// <summary>The chief died and the player must name a new one.</summary>
    public bool AwaitingNewChief { get; set; }

    /// <summary>Bit 0: tutorial on; bits 1..3: one-off tutorial scenes already played.</summary>
    public int TutorialFlags { get => tutorialFlags; set => tutorialFlags = Wrap.Signed8(value); }

    /// <summary>Current mission objective; +50 while its scene plays.</summary>
    public int ObjectiveStage { get => objectiveStage; set => objectiveStage = Wrap.Signed8(value); }

    /// <summary>Current tutorial step.</summary>
    public int TutorialObjective { get => tutorialObjective; set => tutorialObjective = Wrap.Signed8(value); }

    /// <summary>Timer of the current tutorial step.</summary>
    public int TutorialTimer { get => tutorialTimer; set => tutorialTimer = Wrap.Signed8(value); }

    /// <summary>Frame the current objective started at.</summary>
    public int ObjectiveTimer { get; set; }

    /// <summary>Timed unlocks already given.</summary>
    public int UnlocksFired { get => unlocksFired; set => unlocksFired = Wrap.Signed8(value); }

    /// <summary>Bit per tribe: whether its units pick work by themselves.</summary>
    public int BehaviourMask { get => behaviourMask; set => behaviourMask = Wrap.Signed8(value); }

    /// <summary>Whether the units of <paramref name="tribe"/> pick work by themselves (<see cref="BehaviourMask"/>; tribes 0..6).</summary>
    public bool ActsByItself(int tribe) => tribe is >= 0 and < 7 && (BehaviourMask & (1 << tribe)) != 0;

    /// <summary>Units seated by the council scene.</summary>
    public int MeetingCount { get => meetingCount; set => meetingCount = Wrap.Signed8(value); }

    /// <summary>Whether the chief was already inside the chief's hut when the council began.</summary>
    public int ChiefWasInside { get => chiefWasInside; set => chiefWasInside = Wrap.Signed8(value); }

    /// <summary>Saved state of the council's seated units.</summary>
    public MeetingSeat[] Seats { get; } = new MeetingSeat[MeetingSeats];

    internal void Exchange(StateExchange s)
    {
        for (int i = 0; i < ActorSlots; i++)
        {
            actors[i] = s.Value(actors[i]);
        }
        SceneId = s.Value(SceneId);
        StepCount = s.Value(StepCount);
        Step = s.Value(Step);
        FocusUnit = s.Value(FocusUnit);
        SceneKind = s.Value(SceneKind);
        TableBase = s.Value(TableBase);
        Active = s.Flag(Active);
        StepFrame = s.Value(StepFrame);
        ChapterScreen = s.Flag(ChapterScreen);
        AwaitingNewChief = s.Flag(AwaitingNewChief);
        TutorialFlags = s.Value(TutorialFlags);
        ObjectiveStage = s.Value(ObjectiveStage);
        TutorialObjective = s.Value(TutorialObjective);
        TutorialTimer = s.Value(TutorialTimer);
        ObjectiveTimer = s.Value(ObjectiveTimer);
        UnlocksFired = s.Value(UnlocksFired);
        BehaviourMask = s.Value(BehaviourMask);
        MeetingCount = s.Value(MeetingCount);
        ChiefWasInside = s.Value(ChiefWasInside);
        foreach (MeetingSeat seat in Seats)
        {
            seat.Exchange(s);
        }
    }
}

/// <summary>State of the computer player.</summary>
public sealed class AiState
{
    /// <summary>Job slots kept: one per unit slot of the original's computer tribe.</summary>
    public const int JobSlotCount = 50;

    private readonly int[] jobSlots = new int[JobSlotCount];
    private readonly List<GridPoint> route = new();
    private int waveTarget, waveStage, rebuildSlot, intruder, flags;

    /// <summary>
    /// Job number of the computer tribe's unit slot <paramref name="i"/> (<see cref="Unit.SlotInTribe"/>).
    /// A slot beyond the kept ones (a tribe with more units than the original's) has the job of its
    /// own number, as every slot starts with, which is past every post.
    /// </summary>
    public int JobSlot(int i) => i < JobSlotCount ? jobSlots[i] : i;

    /// <summary>Sets the job number of kept unit slot <paramref name="i"/>.</summary>
    public void SetJobSlot(int i, int job) => jobSlots[i] = Wrap.Signed8(job);

    /// <summary>Frame the next attack wave leaves.</summary>
    public int NextWaveFrame { get; set; }

    /// <summary>Frame the current wave left.</summary>
    public int WaveStartFrame { get; set; }

    /// <summary>Target of the current wave; -1 when no wave is out.</summary>
    public int WaveTarget { get => waveTarget; set => waveTarget = Wrap.Signed8(value); }

    /// <summary>Stage of the current wave.</summary>
    public int WaveStage { get => waveStage; set => waveStage = Wrap.Signed8(value); }

    /// <summary>Building plan entry waiting to be rebuilt; -1 none.</summary>
    public int RebuildSlot { get => rebuildSlot; set => rebuildSlot = Wrap.Signed8(value); }

    /// <summary>Player unit inside the computer's territory; -1 none.</summary>
    public int Intruder { get => intruder; set => intruder = value; }

    /// <summary>Traps set (low 6 bits) and scripted flags (bits 6, 7).</summary>
    public int Flags { get => flags; set => flags = Wrap.Signed8(value); }

    /// <summary>In a match: the tribe the current (or last) wave goes for; -1 none.</summary>
    public int WaveTribe { get; set; } = -1;

    /// <summary>In a match: the waypoints of the current wave (the campaign's waves walk the mission's routes).</summary>
    public IReadOnlyList<GridPoint> Route => route;

    /// <summary>Sets the waypoints of the current wave.</summary>
    public void SetRoute(IEnumerable<GridPoint> waypoints)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        route.Clear();
        route.AddRange(waypoints);
    }

    /// <summary>Saves, loads or hashes the state only matches use (format 2 on).</summary>
    internal void ExchangeMatch(StateExchange s)
    {
        WaveTribe = s.Value(WaveTribe);
        GridPoint[] current = [.. route];
        int count = s.Value(current.Length);
        route.Clear();
        for (int i = 0; i < count; i++)
        {
            GridPoint point = i < current.Length ? current[i] : default;
            route.Add(new GridPoint(s.Value(point.X), s.Value(point.Y)));
        }
    }

    internal void Exchange(StateExchange s)
    {
        for (int i = 0; i < JobSlotCount; i++)
        {
            jobSlots[i] = s.Value(jobSlots[i]);
        }
        NextWaveFrame = s.Value(NextWaveFrame);
        WaveStartFrame = s.Value(WaveStartFrame);
        WaveTarget = s.Value(WaveTarget);
        WaveStage = s.Value(WaveStage);
        RebuildSlot = s.Value(RebuildSlot);
        Intruder = s.Value(Intruder);
        Flags = s.Value(Flags);
    }
}

/// <summary>The player's army-wide orders.</summary>
public sealed class ArmyOrders
{
    /// <summary>Rally point column.</summary>
    public int PointX { get; set; }

    /// <summary>Rally point row.</summary>
    public int PointY { get; set; }

    /// <summary>Fighters gather at the point.</summary>
    public bool Rally { get; set; }

    /// <summary>Everyone goes to the point.</summary>
    public bool AllOut { get; set; }

    internal void Exchange(StateExchange s)
    {
        PointX = s.Value(PointX);
        PointY = s.Value(PointY);
        Rally = s.Flag(Rally);
        AllOut = s.Flag(AllOut);
    }
}
