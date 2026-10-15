using System;
using System.Collections.Generic;

namespace Tribes.Client.Ui.Logic;

/// <summary>Why a room or custom game cannot start on its map.</summary>
public enum MatchProblem
{
    /// <summary>It can start.</summary>
    None,

    /// <summary>More tribes than the map has room for camps.</summary>
    MapFull,

    /// <summary>A campaign map with more than two people: it only has the original two tribes.</summary>
    CampaignMapTooManyPeople,
}

/// <summary>
/// How many tribes a match may start with on a map: a custom map has room for as many camps as
/// the rules find on it (Core's <c>MatchStart.CampCapacity</c>); a campaign map only has the
/// original two tribes; an endless world (mission -1) has no limit here. Rooms offer the custom
/// maps only. A tribe's messages (notices, hints) go to its own player.
/// </summary>
public static class MatchRoom
{
    /// <summary>The tribes of a campaign mission: the original player and computer.</summary>
    public const int CampaignMapTribes = 2;

    /// <summary>Whether <paramref name="mission"/> is a campaign mission (before the first custom map).</summary>
    public static bool IsCampaignMap(int mission, int firstCustomMission) => mission >= 0 && mission < firstCustomMission;

    /// <summary>
    /// The most tribes a match on <paramref name="mission"/> starts with: the map's
    /// <paramref name="campCapacity"/> for a custom map, two for a campaign map, no limit for an
    /// endless world (mission -1).
    /// </summary>
    public static int TribeLimit(int mission, int firstCustomMission, int campCapacity) =>
        mission < 0 ? int.MaxValue
        : IsCampaignMap(mission, firstCustomMission) ? CampaignMapTribes
        : Math.Max(0, campCapacity);

    /// <summary>How many computer tribes may still be added: no more than the free slots, nor than the map has room for.</summary>
    public static int ComputersAllowed(int tribeLimit, int freeSlots, int tribes) =>
        Math.Max(0, Math.Min(freeSlots, tribeLimit - tribes));

    /// <summary>
    /// Why a match of <paramref name="tribes"/> tribes (<paramref name="humans"/> of them people)
    /// cannot start on <paramref name="mission"/>, or <see cref="MatchProblem.None"/>.
    /// </summary>
    public static MatchProblem Check(int mission, int firstCustomMission, int campCapacity, int humans, int tribes)
    {
        if (IsCampaignMap(mission, firstCustomMission) && humans > CampaignMapTribes)
        {
            return MatchProblem.CampaignMapTooManyPeople;
        }
        return tribes > TribeLimit(mission, firstCustomMission, campCapacity) ? MatchProblem.MapFull : MatchProblem.None;
    }

    /// <summary>The maps a room offers: the custom maps of the original menu (never a campaign map).</summary>
    public static IReadOnlyList<int> RoomMaps(IReadOnlyList<int> menuMissions, int firstCustomMission) =>
        CampaignChapters.CustomMaps(menuMissions, firstCustomMission);

    /// <summary>
    /// Whether a message for <paramref name="messageTribe"/> is shown to this player, who plays
    /// <paramref name="localTribe"/> (or watches when <paramref name="spectating"/>).
    /// </summary>
    public static bool IsFor(int messageTribe, int localTribe, bool spectating) =>
        !spectating && localTribe >= 0 && messageTribe == localTribe;
}
