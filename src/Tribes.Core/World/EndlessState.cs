using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Tribes.Core.Content;

namespace Tribes.Core.World;

/// <summary>A place where wild animals of an endless world are born and roam.</summary>
public sealed class AnimalHabitat
{
    internal AnimalHabitat(GridPoint cell, int oddsMission, int oddsPoint)
    {
        Cell = cell;
        OddsMission = oddsMission;
        OddsPoint = oddsPoint;
    }

    /// <summary>Its centre.</summary>
    public GridPoint Cell { get; }

    /// <summary>The custom mission whose spawn point gives the kinds born here.</summary>
    public int OddsMission { get; }

    /// <summary>That mission's spawn point.</summary>
    public int OddsPoint { get; }

    /// <summary>Animals of it alive now.</summary>
    public int Animals { get; internal set; }
}

/// <summary>An open invitation to an alliance.</summary>
/// <param name="From">The inviting tribe.</param>
/// <param name="To">The invited tribe.</param>
/// <param name="Frame">The frame it was made.</param>
public readonly record struct AllianceInvitation(int From, int To, int Frame);

/// <summary>
/// The state of an endless world beyond the map and the tribes: its seed (chunks are made from it),
/// how often new computer tribes appear, the animals' habitats and the open alliance invitations.
/// </summary>
public sealed class EndlessState
{
    private readonly List<AnimalHabitat> habitats = new();
    private readonly List<AllianceInvitation> invitations = new();
    private readonly List<LandOrder> landOrders = new();

    internal EndlessState(long seed, int newTribes)
    {
        Seed = seed;
        NewTribes = newTribes;
        Habitats = new ReadOnlyCollection<AnimalHabitat>(habitats);
        Invitations = new ReadOnlyCollection<AllianceInvitation>(invitations);
    }

    /// <summary>The world seed the land is made from.</summary>
    public long Seed { get; private set; }

    /// <summary>How often new computer tribes appear in new land: 0 none .. 3 many.</summary>
    public int NewTribes { get; private set; }

    /// <summary>
    /// Computer tribes the world started with that have no camp yet: each comes into the next land
    /// the people's tribes uncover that has room for it.
    /// </summary>
    public int WaitingTribes { get; internal set; }

    /// <summary>The animals' habitats, in the order they were made.</summary>
    public IReadOnlyList<AnimalHabitat> Habitats { get; }

    /// <summary>Open alliance invitations, oldest first.</summary>
    public IReadOnlyList<AllianceInvitation> Invitations { get; }

    internal AnimalHabitat AddHabitat(GridPoint cell, int oddsMission, int oddsPoint)
    {
        var habitat = new AnimalHabitat(cell, oddsMission, oddsPoint);
        habitats.Add(habitat);
        return habitat;
    }

    /// <summary>Chunks being made, in the order they were asked for (and join the world).</summary>
    internal IReadOnlyList<LandOrder> LandOrders => landOrders;

    internal void Order(LandOrder order) => landOrders.Add(order);

    /// <summary>The first chunk being made joined the world.</summary>
    internal void RemoveFirstOrder() => landOrders.RemoveAt(0);

    internal void Invite(AllianceInvitation invitation) => invitations.Add(invitation);

    internal void RemoveInvitations(Predicate<AllianceInvitation> match) => invitations.RemoveAll(match);

    internal void Exchange(StateExchange s)
    {
        Seed = s.Value64(Seed);
        NewTribes = s.Value(NewTribes);
        WaitingTribes = s.Value(WaitingTribes);
        if (WaitingTribes < 0)
        {
            throw new System.IO.InvalidDataException($"endless state: {WaitingTribes} waiting tribes");
        }
        int count = s.Value(habitats.Count);
        if (s.IsReading)
        {
            habitats.Clear();
            for (int i = 0; i < count; i++)
            {
                var cell = new GridPoint(s.Value(0), s.Value(0));
                AddHabitat(cell, s.Value(0), s.Value(0)).Animals = s.Value(0);
            }
        }
        else
        {
            foreach (AnimalHabitat habitat in habitats)
            {
                s.Value(habitat.Cell.X);
                s.Value(habitat.Cell.Y);
                s.Value(habitat.OddsMission);
                s.Value(habitat.OddsPoint);
                s.Value(habitat.Animals);
            }
        }
        ExchangeInvitations(s);
        ExchangeLandOrders(s);
    }

    private void ExchangeLandOrders(StateExchange s)
    {
        int count = s.Value(landOrders.Count);
        if (s.IsReading)
        {
            landOrders.Clear();
            for (int i = 0; i < count; i++)
            {
                landOrders.Add(LandOrder.Exchange(s, null));
            }
            return;
        }
        foreach (LandOrder order in landOrders)
        {
            LandOrder.Exchange(s, order);
        }
    }

    private void ExchangeInvitations(StateExchange s)
    {
        int count = s.Value(invitations.Count);
        if (s.IsReading)
        {
            invitations.Clear();
            for (int i = 0; i < count; i++)
            {
                invitations.Add(new AllianceInvitation(s.Value(0), s.Value(0), s.Value(0)));
            }
            return;
        }
        foreach (AllianceInvitation invitation in invitations)
        {
            s.Value(invitation.From);
            s.Value(invitation.To);
            s.Value(invitation.Frame);
        }
    }
}
