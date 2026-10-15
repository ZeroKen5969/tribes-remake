using System;
using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Endless;
using Tribes.Core.Systems.Match;
using Tribes.Core.World;
using Tribes.Net;
using CoreMatchStart = Tribes.Core.Systems.Match.MatchStart;

namespace Tribes.Session;

/// <summary>
/// Matches of the multi-tribe rules: every slot holder plays a tribe of its own (people by slot,
/// computer players at their strength), each with a camp. Versus matches are played on the custom
/// maps, team mates are allies. Endless worlds are made as they are played
/// (<see cref="GameSimulation.StartEndless"/>): their computer tribes come at the start and later in
/// new land, they never end, and a player who joins later always gets a new tribe and camp. The
/// rules keep who controls each tribe, so absent and finished players' tribes take no orders and the
/// computer never plays them. Campaign maps fall back to the original two tribes
/// (<see cref="OriginalTribeControl"/>).
/// </summary>
public sealed class MultiTribeControl : ITribeControl
{
    private const int HardestComputer = 1;

    /// <summary>The density of new computer tribes an endless world played alone gets unless told otherwise.</summary>
    public const int SteadyNewTribes = 2;

    /// <summary>The shared instance.</summary>
    public static MultiTribeControl Instance { get; } = new();

    /// <inheritdoc />
    public MatchWorld Start(GameContent content, MatchPlan plan, IReadOnlyList<PlayerInfo> roster)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(roster);
        if (plan.LoadWorld(content) is { } continued)
        {
            return Continue(continued, roster);
        }
        if (plan.Endless is { } endless)
        {
            return StartEndless(content, plan, endless, roster);
        }
        int map = plan.MapMission - CoreMatchStart.FirstMapMission;
        if (map < 0 || map >= CoreMatchStart.MapCount(content))
        {
            return OriginalTribeControl.Instance.Start(content, plan, roster);
        }
        var tribes = new List<MatchTribe>();
        int[] tribeOfSlot = NoTribes();
        foreach (PlayerInfo player in BySlot(roster))
        {
            tribeOfSlot[player.Slot] = tribes.Count;
            tribes.Add(new MatchTribe(ControllerOf(player), player.Team));
        }
        GameSimulation simulation = GameSimulation.StartMatch(content, new MatchSetup
        {
            Map = map,
            Seed = plan.Seed,
            Difficulty = plan.Difficulty,
            Tribes = tribes,
            HintsEnabled = true,
            Settings = plan.Settings ?? MatchPlan.DefaultMatchSettings,
        }, new SimulationOptions { HintsPauseGame = false });
        return new MatchWorld(simulation, tribeOfSlot);
    }

    /// <summary>
    /// A new endless world for one player alone: the player's tribe (slot 0) and
    /// <paramref name="computerTribes"/> computer tribes (any number), each with a camp; new computer
    /// tribes appear in new land by <paramref name="newTribes"/> (0 none .. 3 many); the start area is
    /// <paramref name="startAreaSize"/> cells square (0: the rules' default). Hints stop the world as
    /// in the single-player game.
    /// </summary>
    public static GameSimulation NewEndlessWorld(GameContent content, long seed, int difficulty, int computerTribes, bool hints,
        int newTribes = SteadyNewTribes, int startAreaSize = 0, WorldSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return GameSimulation.StartEndless(content, new EndlessSetup
        {
            Seed = seed,
            Difficulty = difficulty,
            Tribes = [new MatchTribe(TribeController.Human(0))],
            ComputerTribes = Math.Max(0, computerTribes),
            NewTribes = Math.Clamp(newTribes, 0, EndlessStart.MostNewTribes),
            StartAreaSize = Math.Max(0, startAreaSize),
            HintsEnabled = hints,
            Settings = settings,
        }, new SimulationOptions { HintsPauseGame = true });
    }

    /// <summary>A new endless network world: every slot holder's tribe (no teams), then the plan's computer tribes.</summary>
    private static MatchWorld StartEndless(GameContent content, MatchPlan plan, EndlessPlan endless, IReadOnlyList<PlayerInfo> roster)
    {
        var tribes = new List<MatchTribe>();
        int[] tribeOfSlot = NoTribes();
        foreach (PlayerInfo player in BySlot(roster))
        {
            tribeOfSlot[player.Slot] = tribes.Count;
            tribes.Add(new MatchTribe(ControllerOf(player)));
        }
        if (tribes.Count == 0)
        {
            // Nobody holds a slot (only watchers): the world still needs a first tribe.
            tribes.Add(new MatchTribe(TribeController.Computer()));
        }
        GameSimulation simulation = GameSimulation.StartEndless(content, new EndlessSetup
        {
            Seed = endless.Seed != 0 ? endless.Seed : plan.Seed,
            Difficulty = plan.Difficulty,
            Tribes = tribes,
            ComputerTribes = Math.Max(0, endless.ComputerTribes),
            NewTribes = Math.Clamp(endless.NewTribes, 0, EndlessStart.MostNewTribes),
            StartAreaSize = Math.Max(0, endless.StartAreaSize),
            DevAllianceHalls = endless.DevAllianceHalls,
            Settings = plan.Settings,
        }, new SimulationOptions { HintsPauseGame = false });
        return new MatchWorld(simulation, tribeOfSlot);
    }

    /// <inheritdoc />
    public void SetPlayerState(GameSimulation simulation, int tribe, int slot, PlayerState state)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (!simulation.IsMatch || tribe < 0 || tribe >= simulation.World.Tribes.Count
            || !simulation.World.Tribes[tribe].Controller.IsHumanRuled)
        {
            return;
        }
        TribeController? controller = state switch
        {
            PlayerState.Playing => TribeController.Human(slot),
            PlayerState.Absent => TribeController.Absent(slot),
            PlayerState.Forfeited => TribeController.Forfeited(slot),
            _ => null,
        };
        if (controller is { } next && simulation.World.Tribes[tribe].Controller != next
            && simulation.World.Tribes[tribe].Controller.Kind != TribeControl.Forfeited)
        {
            simulation.SetController(tribe, next);
        }
    }

    /// <inheritdoc />
    public int AddTribe(GameSimulation simulation, int slot, int team)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (!simulation.IsMatch)
        {
            return -1;
        }
        // Endless worlds have no teams: a newcomer is nobody's ally until an alliance is made.
        int side = simulation.World.Match.EndsWithLastSide ? team : TribeState.NoTeam;
        return simulation.AddTribe(TribeController.Human(slot), side);
    }

    /// <inheritdoc />
    public bool IsDefeated(GameSimulation simulation, int tribe)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return simulation.IsMatch ? simulation.IsDefeated(tribe) : OriginalTribeControl.Instance.IsDefeated(simulation, tribe);
    }

    /// <inheritdoc />
    public MatchOutcome? Outcome(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (!simulation.IsMatch)
        {
            return OriginalTribeControl.Instance.Outcome(simulation);
        }
        MatchResult result = simulation.Match;
        return result.IsDecided ? new MatchOutcome([.. result.Winners]) : null;
    }

    /// <summary>The slots of a world that goes on (a game played alone opened to others): each human slot finds its tribe.</summary>
    private static MatchWorld Continue(GameSimulation simulation, IReadOnlyList<PlayerInfo> roster)
    {
        int[] tribeOfSlot = NoTribes();
        var taken = new HashSet<int>();
        ListView<TribeState> tribes = simulation.World.Tribes;
        foreach (PlayerInfo player in BySlot(roster))
        {
            for (int t = 0; t < tribes.Count; t++)
            {
                TribeController controller = tribes[t].Controller;
                bool fits = player.Kind == PlayerKind.Computer
                    ? controller.Kind == TribeControl.Computer
                    : controller.IsHumanRuled && controller.Slot == player.Slot;
                if (fits && taken.Add(t))
                {
                    tribeOfSlot[player.Slot] = t;
                    break;
                }
            }
        }
        return new MatchWorld(simulation, tribeOfSlot);
    }

    private static TribeController ControllerOf(PlayerInfo player)
    {
        if (player.Kind == PlayerKind.Computer)
        {
            return TribeController.Computer(Math.Clamp(player.AiLevel, 0, HardestComputer));
        }
        return player.Presence switch
        {
            PlayerPresence.Absent => TribeController.Absent(player.Slot),
            PlayerPresence.Forfeited => TribeController.Forfeited(player.Slot),
            _ => TribeController.Human(player.Slot),
        };
    }

    private static List<PlayerInfo> BySlot(IReadOnlyList<PlayerInfo> roster)
    {
        var slotted = new List<PlayerInfo>(roster);
        slotted.RemoveAll(p => !p.HasSlot);
        slotted.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        return slotted;
    }

    private static int[] NoTribes()
    {
        var tribes = new int[RoomLimits.MaxPlayers];
        Array.Fill(tribes, -1);
        return tribes;
    }
}
