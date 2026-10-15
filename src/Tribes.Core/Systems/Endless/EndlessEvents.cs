using System.Collections.Generic;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Endless;

/// <summary>New land was made (in this order): draw it, put it on the minimap.</summary>
/// <param name="Chunks">The chunks made.</param>
public sealed record ChunksGenerated(IReadOnlyList<ChunkCoord> Chunks) : GameEvent;

/// <summary>A tribe got its camp: at the start, joining the game, or a new computer tribe in new land.</summary>
/// <param name="Tribe">The tribe.</param>
/// <param name="Camp">Its camp's origin (the chief's hut's cell).</param>
/// <param name="Computer">Whether the computer plays it.</param>
public sealed record TribeAppeared(int Tribe, GridPoint Camp, bool Computer) : GameEvent;

/// <summary><paramref name="From"/> invited <paramref name="To"/> into an alliance.</summary>
/// <param name="From">The inviting tribe.</param>
/// <param name="To">The invited tribe.</param>
public sealed record AllianceInvited(int From, int To) : GameEvent;

/// <summary><paramref name="To"/> turned down the invitation of <paramref name="From"/>.</summary>
/// <param name="From">The inviting tribe.</param>
/// <param name="To">The invited tribe.</param>
public sealed record AllianceDeclined(int From, int To) : GameEvent;

/// <summary>The invitation of <paramref name="From"/> to <paramref name="To"/> lapsed or could no longer be taken.</summary>
/// <param name="From">The inviting tribe.</param>
/// <param name="To">The invited tribe.</param>
public sealed record AllianceInvitationLapsed(int From, int To) : GameEvent;

/// <summary><paramref name="A"/> and <paramref name="B"/> are allies now.</summary>
/// <param name="A">The inviting tribe.</param>
/// <param name="B">The tribe that accepted.</param>
public sealed record AllianceFormed(int A, int B) : GameEvent;

/// <summary><paramref name="A"/> and <paramref name="B"/> are no longer allies: <paramref name="By"/> left the alliance or is out.</summary>
/// <param name="A">One of the two.</param>
/// <param name="B">The other.</param>
/// <param name="By">The tribe that left, or that is out (wiped out or given up).</param>
public sealed record AllianceEnded(int A, int B, int By) : GameEvent;

/// <summary><paramref name="From"/> gave <paramref name="To"/> <paramref name="Amount"/> of <paramref name="Good"/>.</summary>
/// <param name="From">The giving tribe.</param>
/// <param name="To">The ally receiving.</param>
/// <param name="Good">The store counter given.</param>
/// <param name="Amount">How much arrived.</param>
public sealed record GoodsTransferred(int From, int To, TribeCounter Good, int Amount) : GameEvent;
