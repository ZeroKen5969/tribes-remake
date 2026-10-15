using System;
using System.Collections.Generic;

namespace Tribes.Core.World;

/// <summary>
/// Who is allied with whom. Tribes on the same team (<see cref="TribeState.Team"/> 0 or more) are
/// allies, and so are pairs allied on their own (an alliance made during the game); every other
/// pair of different tribes are enemies. Allies do not target or hurt each other.
/// </summary>
public sealed class TribeRelations
{
    private readonly IReadOnlyList<TribeState> tribes;
    private readonly SortedSet<(int Low, int High)> alliances = new();

    internal TribeRelations(IReadOnlyList<TribeState> tribes) =>
        this.tribes = tribes ?? throw new ArgumentNullException(nameof(tribes));

    /// <summary>Pairs allied on their own (not through a team), lower tribe first, in order.</summary>
    public IReadOnlyCollection<(int Low, int High)> Alliances => alliances;

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are the same tribe or allies.</summary>
    public bool AreAllies(int a, int b)
    {
        if (a == b)
        {
            return true;
        }
        if (!IsTribe(a) || !IsTribe(b))
        {
            return false;
        }
        int teamA = tribes[a].Team;
        return (teamA >= 0 && teamA == tribes[b].Team) || alliances.Contains(Pair(a, b));
    }

    /// <summary>Whether <paramref name="a"/> and <paramref name="b"/> are tribes at war with each other.</summary>
    public bool AreEnemies(int a, int b) => a != b && IsTribe(a) && IsTribe(b) && !AreAllies(a, b);

    /// <summary>Grows whenever who is allied with whom may have changed (alliances, teams, tribes); not part of the state.</summary>
    internal int Version { get; private set; }

    /// <summary>Notes that the relations may have changed (a team was set).</summary>
    internal void Changed() => Version++;

    /// <summary>Makes <paramref name="a"/> and <paramref name="b"/> allies (or ends their own alliance; team mates stay allies).</summary>
    public void SetAlliance(int a, int b, bool allied)
    {
        if (!IsTribe(a) || !IsTribe(b) || a == b)
        {
            throw new ArgumentOutOfRangeException(nameof(b), b, "an alliance needs two different tribes of the world");
        }
        if (allied)
        {
            alliances.Add(Pair(a, b));
        }
        else
        {
            alliances.Remove(Pair(a, b));
        }
        Version++;
    }

    private bool IsTribe(int tribe) => tribe >= 0 && tribe < tribes.Count;

    private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);

    internal void Exchange(StateExchange s)
    {
        (int Low, int High)[] current = [.. alliances];
        Version++;
        int count = s.Value(current.Length);
        alliances.Clear();
        for (int i = 0; i < count; i++)
        {
            (int low, int high) = i < current.Length ? current[i] : (0, 0);
            alliances.Add((s.Value(low), s.Value(high)));
        }
    }
}
