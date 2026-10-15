using System;
using System.Collections.Generic;

namespace Tribes.Core.World;

/// <summary>
/// The role each tribe plays, the single place the rules ask. The original has two tribes: tribe 0
/// the player (the only one with fog of war, hints, messages, army calls and a chief), tribe 1 the
/// computer, each the other's enemy. Here every tribe has a controller
/// (<see cref="TribeState.Controller"/>): the rules the original gives the player apply to every
/// human-ruled tribe (present, absent or forfeited), those of the computer to every computer
/// tribe; only present humans give orders and only computer tribes run the AI. Who fights whom
/// follows <see cref="GameWorld.Relations"/>. With the original two tribes every answer is the
/// original's. Members are only ever added.
/// </summary>
public static class TribeRoles
{
    private const int OriginalPlayer = 0;
    private const int OriginalComputer = 1;

    /// <summary>
    /// The tribe of the single-player campaign's player: the first human-ruled tribe (tribe 0 when
    /// there is none). Rules of the whole world use it only for the campaign's story; everything a
    /// tribe does for itself asks <see cref="IsPlayer"/>.
    /// </summary>
    public static int Player(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ListView<TribeState> tribes = world.Tribes;
        for (int t = 0; t < tribes.Count; t++)
        {
            if (tribes[t].Controller.IsHumanRuled)
            {
                return t;
            }
        }
        return OriginalPlayer;
    }

    /// <summary>Whether <paramref name="tribe"/> is human-ruled: it keeps the player's rules (fog, hints, chief, army calls).</summary>
    public static bool IsPlayer(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return tribe >= 0 && tribe < world.Tribes.Count && world.Tribes[tribe].Controller.IsHumanRuled;
    }

    /// <summary>
    /// The tribe of the single-player campaign's computer: the first computer tribe (tribe 1 when
    /// there is none, or tribe 0 in a world of one tribe).
    /// </summary>
    public static int Computer(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ListView<TribeState> tribes = world.Tribes;
        for (int t = 0; t < tribes.Count; t++)
        {
            if (tribes[t].Controller.RunsAi)
            {
                return t;
            }
        }
        return Math.Min(OriginalComputer, tribes.Count - 1);
    }

    /// <summary>Whether the computer plays <paramref name="tribe"/>: it keeps the computer's rules and runs the AI.</summary>
    public static bool IsComputer(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return tribe >= 0 && tribe < world.Tribes.Count && world.Tribes[tribe].Controller.RunsAi;
    }

    /// <summary>Whether <paramref name="tribe"/> takes player orders now (a present human).</summary>
    public static bool TakesOrders(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return tribe >= 0 && tribe < world.Tribes.Count && world.Tribes[tribe].Controller.TakesOrders;
    }

    /// <summary>The difficulty the computer plays <paramref name="tribe"/> at (its own, else the world's).</summary>
    public static int Difficulty(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return tribe >= 0 && tribe < world.Tribes.Count && world.Tribes[tribe].Controller.Difficulty is int own
            ? own
            : world.Difficulty;
    }

    /// <summary>
    /// The tribe whose player state (army calls, hints, chief, first building) the rules of
    /// <paramref name="tribe"/> read: the tribe itself in a match; in the campaign the player's,
    /// which the original keeps for the whole world.
    /// </summary>
    public static int PlayerOf(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Mode == WorldMode.Match ? tribe : Player(world);
    }

    /// <summary>The army-wide orders <paramref name="tribe"/>'s units follow (those of <see cref="PlayerOf"/>).</summary>
    public static ArmyOrders Army(GameWorld world, int tribe)
    {
        int owner = PlayerOf(world, tribe);
        return world.Tribes[owner].Army;
    }

    /// <summary>
    /// Frames since the fighting that <paramref name="tribe"/>'s quiet-time rules (new chief, work of
    /// a unit's own choice, hints) wait for: in a match the tribe's own fighting (as attacker or
    /// attacked); in the campaign anyone's, as the original counts for the whole world.
    /// </summary>
    public static int FramesSinceCombat(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Mode == WorldMode.Match && tribe >= 0 && tribe < world.Tribes.Count
            ? world.Tribes[tribe].FramesSinceCombat
            : world.FramesSinceCombat;
    }

    /// <summary>
    /// The computer player state <paramref name="tribe"/>'s rules read: the tribe's own in a match;
    /// in the campaign the computer's, which the original keeps for the whole world.
    /// </summary>
    public static AiState Ai(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Tribes[world.Mode == WorldMode.Match ? tribe : Computer(world)].Ai;
    }

    /// <summary>
    /// The hint state of the player of <paramref name="tribe"/>: the tribe's own in a match; in the
    /// campaign the player's, which the original keeps for the whole world.
    /// </summary>
    public static HintState Hints(GameWorld world, int tribe)
    {
        int owner = PlayerOf(world, tribe);
        return world.Tribes[owner].Hints;
    }

    /// <summary>
    /// The tribe credited with killing a unit or destroying a building of <paramref name="victim"/>
    /// (its statistics), or -1: in the campaign the other tribe, whatever the cause, as the
    /// original counts; in a match <paramref name="killer"/> when it is an enemy of the victim.
    /// </summary>
    public static int CreditedFor(GameWorld world, int victim, int killer)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Mode == WorldMode.Campaign)
        {
            return Enemy(world, victim);
        }
        return AreEnemies(world, victim, killer) ? killer : -1;
    }

    /// <summary>
    /// Whether the player of <paramref name="tribe"/> is asked to name a new chief: the tribe's own
    /// question in a match; in the campaign the player's, which the original keeps for the world.
    /// </summary>
    public static bool AwaitsNewChief(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Mode == WorldMode.Match ? world.Tribes[tribe].AwaitingNewChief : world.Script.AwaitingNewChief;
    }

    /// <summary>The player of <paramref name="tribe"/> named a new chief (see <see cref="AwaitsNewChief"/>).</summary>
    internal static void ChiefNamed(GameWorld world, int tribe)
    {
        if (world.Mode == WorldMode.Match)
        {
            world.Tribes[tribe].AwaitingNewChief = false;
        }
        else
        {
            world.Script.AwaitingNewChief = false;
        }
    }

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are tribes at war.</summary>
    public static bool AreEnemies(GameWorld world, int a, int b)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Relations.AreEnemies(a, b);
    }

    /// <summary>The enemies of <paramref name="tribe"/>, in tribe order (walking them with <c>foreach</c> allocates nothing).</summary>
    public static EnemyTribes EnemiesOf(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new EnemyTribes(world, tribe);
    }

    /// <summary>The human-ruled tribes, in tribe order.</summary>
    public static IEnumerable<int> HumanTribes(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        for (int t = 0; t < world.Tribes.Count; t++)
        {
            if (world.Tribes[t].Controller.IsHumanRuled)
            {
                yield return t;
            }
        }
    }

    /// <summary>The computer tribes, in tribe order.</summary>
    public static IEnumerable<int> ComputerTribes(GameWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        for (int t = 0; t < world.Tribes.Count; t++)
        {
            if (world.Tribes[t].Controller.RunsAi)
            {
                yield return t;
            }
        }
    }

    /// <summary>
    /// The enemy of <paramref name="tribe"/> in the original's two-tribe sense: its first enemy
    /// (the other tribe of two). Kept for callers outside the rules; the rules ask
    /// <see cref="AreEnemies"/> or <see cref="EnemiesOf"/>.
    /// </summary>
    public static int Enemy(GameWorld world, int tribe)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (int enemy in EnemiesOf(world, tribe))
        {
            return enemy;
        }
        return tribe ^ 1;
    }
}

/// <summary>The enemies of a tribe, in tribe order (<see cref="TribeRoles.EnemiesOf"/>).</summary>
public readonly struct EnemyTribes : IEnumerable<int>
{
    private readonly GameWorld world;
    private readonly int tribe;

    internal EnemyTribes(GameWorld world, int tribe) => (this.world, this.tribe) = (world, tribe);

    /// <summary>An enumerator over the enemies (a struct: <c>foreach</c> allocates nothing).</summary>
    public Enumerator GetEnumerator() => new(world, tribe);

    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Walks the enemies of a tribe.</summary>
    public struct Enumerator : IEnumerator<int>
    {
        private readonly GameWorld world;
        private readonly int tribe;
        private int current;

        internal Enumerator(GameWorld world, int tribe) => (this.world, this.tribe, current) = (world, tribe, -1);

        /// <inheritdoc />
        public readonly int Current => current;

        readonly object System.Collections.IEnumerator.Current => current;

        /// <inheritdoc />
        public bool MoveNext()
        {
            while (++current < world.Tribes.Count)
            {
                if (world.Relations.AreEnemies(tribe, current))
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc />
        public void Reset() => current = -1;

        /// <inheritdoc />
        public readonly void Dispose()
        {
        }
    }
}
