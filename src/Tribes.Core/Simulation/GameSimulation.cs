using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Tribes.Core.Commands;
using Tribes.Core.Content;
using Tribes.Core.Systems.Missions;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;

namespace Tribes.Core.Simulation;

/// <summary>
/// A game in progress, run one tick at a time. A tick is one pass of the game loop: the players'
/// orders are carried out first, then, depending on the game's state, one world frame runs
/// (normal play), one display frame of the story scene shown runs (it may run a staged world
/// frame), or nothing more (the host paused, or the mission is over). Everything a tick does
/// depends only on the state and the tick's input, so peers that start alike and get the same
/// inputs stay alike; <see cref="StateHash"/> lets them check that.
/// </summary>
public sealed partial class GameSimulation
{
    private const int SaveMagic = 0x53425254; // "TRBS"
    private const int SaveVersion = 1;
    private const int HintsPauseFlag = 1;

    private readonly RuleSystems systems;

    private GameSimulation(RuleSystems systems, long tick, SimulationOptions options)
    {
        this.systems = systems;
        Tick = tick;
        Options = options;
    }

    /// <summary>The world (read it to show the game; change it only through ticks).</summary>
    public GameWorld World => systems.World;

    /// <summary>The next tick to run, which is also the number of ticks run so far.</summary>
    public long Tick { get; private set; }

    /// <summary>The choices this game keeps.</summary>
    public SimulationOptions Options { get; }

    /// <summary>Whether the mission's result has been announced; no tick changes anything after that.</summary>
    public bool IsOver => World.EndCountdown is MissionOutcome.VictoryShown or MissionOutcome.DefeatShown;

    /// <summary>The mission's result once it is over (true won, false lost); null while it goes on.</summary>
    public bool? Victory => IsOver ? World.EndCountdown == MissionOutcome.VictoryShown : null;

    /// <summary>What the next tick that is not paused does.</summary>
    public TickKind NextKind => IsOver ? TickKind.Over : World.Script.Active ? TickKind.Scene : TickKind.Play;

    /// <summary>The story step shown, or null when no scene is playing.</summary>
    public SceneStep? CurrentScene => systems.CurrentSceneStep;

    /// <summary>Whether a tap on the scene shown is taken now (see <see cref="AdvanceScene"/>).</summary>
    public bool CanContinueScene => systems.CanContinueScene;

    /// <summary>The rule systems (for tests and tools in this assembly's friends).</summary>
    internal RuleSystems Rules => systems;

    /// <summary>
    /// A new game of mission <paramref name="mission"/> at <paramref name="difficulty"/> (0 easy,
    /// 1 hard). When the mission opens with a dialogue, its scene is shown first
    /// (<see cref="CurrentScene"/>); the events of the start come with the first tick.
    /// </summary>
    public static GameSimulation StartMission(GameContent content, int mission, int difficulty,
        MissionOptions? missionOptions = null, SimulationOptions? options = null) =>
        new(MissionStart.Begin(content, mission, difficulty, missionOptions), 0, options ?? new SimulationOptions());

    /// <summary>A game saved by <see cref="Save(Stream)"/>; <paramref name="content"/> must be the game's content.</summary>
    /// <exception cref="InvalidDataException">The data is not a saved game of this format.</exception>
    public static GameSimulation Load(GameContent content, Stream input) => Load(content, input, null);

    /// <summary>A game saved by <see cref="Save()"/>.</summary>
    public static GameSimulation Load(GameContent content, byte[] data) => Load(content, data, null);

    /// <summary>
    /// A game saved by <see cref="Save()"/> that goes on with <paramref name="options"/> instead of
    /// the saved ones (a game played alone that others join), or with the saved ones when null.
    /// </summary>
    public static GameSimulation Load(GameContent content, byte[] data, SimulationOptions? options)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var input = new MemoryStream(data, writable: false);
        return Load(content, input, options);
    }

    /// <summary>
    /// A game saved by <see cref="Save(Stream)"/> that goes on with <paramref name="options"/>
    /// instead of the saved ones, or with the saved ones when null.
    /// </summary>
    /// <exception cref="InvalidDataException">The data is not a saved game of this format.</exception>
    public static GameSimulation Load(GameContent content, Stream input, SimulationOptions? options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(input);
        long tick;
        int flags;
        using (var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true))
        {
            if (reader.ReadInt32() != SaveMagic)
            {
                throw new InvalidDataException("not a saved game");
            }
            int version = reader.ReadInt32();
            // Every format from 1 to this build's is read (saves outlive updates); newer ones need a newer game.
            if (version > SaveVersion)
            {
                throw new InvalidDataException($"saved game format {version} is newer than this game reads ({SaveVersion})");
            }
            if (version < 1)
            {
                throw new InvalidDataException($"saved game format {version} is not supported");
            }
            tick = reader.ReadInt64();
            flags = reader.ReadInt32();
        }
        GameWorld world = WorldSnapshot.Load(input, content);
        return new GameSimulation(new RuleSystems(world), tick,
            options ?? new SimulationOptions { HintsPauseGame = (flags & HintsPauseFlag) != 0 });
    }

    /// <summary>A game going on from <paramref name="world"/> at tick <paramref name="tick"/>.</summary>
    internal static GameSimulation FromWorld(GameWorld world, long tick = 0, SimulationOptions? options = null) =>
        new(new RuleSystems(world), tick, options ?? new SimulationOptions());

    /// <summary>Writes the whole game (world, tick and options) to <paramref name="output"/>.</summary>
    public void Save(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(SaveMagic);
            writer.Write(SaveVersion);
            writer.Write(Tick);
            writer.Write(Options.HintsPauseGame ? HintsPauseFlag : 0);
        }
        WorldSnapshot.Save(World, output);
    }

    /// <summary>The whole game as bytes (see <see cref="Load(GameContent, byte[])"/>).</summary>
    public byte[] Save()
    {
        using var output = new MemoryStream();
        Save(output);
        return output.ToArray();
    }

    /// <summary>A hash of the whole rule state (<see cref="WorldSnapshot.Hash"/>); equal games have equal hashes.</summary>
    public ulong StateHash() => WorldSnapshot.Hash(World);

    /// <summary>Runs a tick without orders.</summary>
    public TickResult Step() => Step(TickInput.Idle);

    /// <summary>Runs tick <see cref="Tick"/> with <paramref name="input"/> and moves on to the next one.</summary>
    public TickResult Step(TickInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach (PlayerOrder order in input.Orders)
        {
            ArgumentNullException.ThrowIfNull(order, nameof(input));
        }
        long tick = Tick++;
        if (IsOver)
        {
            return new TickResult(tick, TickKind.Over, false, Refused(input.Orders.Count), systems.TakeEvents(), null);
        }
        bool sceneShown = World.Script.Active;
        int firstEvent = systems.PendingEvents.Count;
        IReadOnlyList<OrderOutcome> outcomes = ApplyOrders(input.Orders);
        var inputs = new TutorialInputs(input.RosterOpen);
        TickKind kind;
        bool advanced = false;
        bool? victory = null;
        if (input.Paused)
        {
            kind = TickKind.Paused;
        }
        else if (sceneShown)
        {
            kind = TickKind.Scene;
            (advanced, victory) = SceneFrame(inputs);
        }
        else
        {
            kind = TickKind.Play;
            if (!World.Script.Active && !(Options.HintsPauseGame && systems.WorldFrame.HintShownSince(firstEvent)))
            {
                victory = RunWorldFrame(staged: false, inputs);
                advanced = true;
            }
        }
        return new TickResult(tick, kind, advanced, outcomes, systems.TakeEvents(), victory);
    }

    /// <summary>
    /// One world frame on its own (<paramref name="staged"/>: a frame a scene step runs). Returns
    /// the mission's result when it was announced.
    /// </summary>
    internal bool? RunWorldFrame(bool staged, TutorialInputs inputs) =>
        systems.WorldFrame.Run(staged, inputs, Options.HintsPauseGame);

    /// <summary>
    /// One display frame of the scene shown: a step that went on by itself on the last frame goes
    /// on now (unless a tap already did), then the step shown runs its frame.
    /// </summary>
    private (bool Advanced, bool? Victory) SceneFrame(TutorialInputs inputs)
    {
        ScriptState script = World.Script;
        if (script.Active && script.StepFrame == ScenePlayer.ContinuesByItself)
        {
            systems.ContinueScene();
        }
        if (!script.Active || systems.AdvanceSceneStep() != Systems.Story.SceneFrame.AdvanceWorld)
        {
            return (false, null);
        }
        return (true, RunWorldFrame(staged: true, inputs));
    }

    private OrderOutcome[] ApplyOrders(IReadOnlyList<PlayerOrder> orders)
    {
        var outcomes = new OrderOutcome[orders.Count];
        for (int i = 0; i < orders.Count; i++)
        {
            outcomes[i] = orders[i].ApplyTo(systems);
        }
        return outcomes;
    }

    private static OrderOutcome[] Refused(int count)
    {
        var outcomes = new OrderOutcome[count];
        Array.Fill(outcomes, OrderOutcome.NotAvailable);
        return outcomes;
    }
}
