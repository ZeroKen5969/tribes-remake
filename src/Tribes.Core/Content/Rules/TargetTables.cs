using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Core.Content.Rules;

/// <summary>
/// A category of cell contents, decoded from the original's cell codes: 0 empty, 1 unit,
/// 2 building, 3 resource node (carcasses included), 4 terrain resource (a tree or a rock cell),
/// 5 animal, 6 anything else (blocked cells, traps).
/// </summary>
/// <param name="Id">Category number 0..6; codes are matched against the categories in order.</param>
/// <param name="CodeMin">Smallest cell code of the category.</param>
/// <param name="CodeMax">Largest cell code of the category.</param>
/// <param name="IndexMultiplier">The entity slot is code * IndexMultiplier + IndexOffset.</param>
/// <param name="IndexOffset">See <see cref="IndexMultiplier"/>.</param>
/// <param name="SubKindProperty">
/// The property giving the content's sub-kind (building kind, node kind); a constant zero for the others.
/// </param>
/// <param name="SideDivisor">
/// The content belongs to the other side when ((slot - 1) / SideDivisor + viewing tribe) is odd;
/// 127 for contents of no side.
/// </param>
/// <param name="HitPointsProperty">
/// The property that is at most 0 once the content is destroyed, or null when it cannot be destroyed.
/// </param>
/// <param name="UnknownProperty">
/// A column nothing reads; its values look like the pixel offset properties. Kept as data.
/// </param>
/// <param name="PositionProperty">The content's column property (the row is the next property), or null.</param>
public sealed record CellCategory(int Id, int CodeMin, int CodeMax, int IndexMultiplier, int IndexOffset,
    EntityProperty SubKindProperty, int SideDivisor, EntityProperty? HitPointsProperty,
    EntityProperty? UnknownProperty, EntityProperty? PositionProperty);

/// <summary>
/// The action a unit takes when sent to a cell of its own side or of no side, for the cell
/// categories that do not depend on a building or node kind (those are
/// <see cref="BuildingOperation.UseAction"/> and <see cref="TargetTables.GatherActionByNodeKind"/>).
/// </summary>
/// <param name="EmptyCell">Sent to an empty cell.</param>
/// <param name="OwnUnit">Sent to a tribe mate.</param>
/// <param name="TerrainResource">Sent to a terrain resource, by its index: 0 a tree, 1 a rock.</param>
/// <param name="Animal">Sent to an animal.</param>
/// <param name="Other">Sent to anything else.</param>
/// <param name="EnemyAttack">
/// Sent to an enemy: 0 a unit, 1 a building; a re-targeting order takes the next action
/// (children are captured with action 107 instead).
/// </param>
public sealed record TargetActions(int EmptyCell, int OwnUnit, IReadOnlyList<int> TerrainResource, int Animal,
    int Other, IReadOnlyList<int> EnemyAttack);

/// <summary>How an idle unit answers a threat it has found.</summary>
/// <param name="Threat">Threat type: 0 none, 1 a child, 2 an animal, 3 a building, 4 an adult unit.</param>
/// <param name="Action">The action it attacks with.</param>
/// <param name="CellCodeSign">The target slot is the cell code times this (an animal's slot is then reduced by 101).</param>
public sealed record ThreatResponse(int Threat, int Action, int CellCodeSign);

/// <summary>
/// A group of entities searched as a whole for targets and threats: 0 tribe 0's units, 1 tribe
/// 1's units, 2 tribe 0's buildings, 3 tribe 1's buildings, 4 the animals.
/// </summary>
/// <param name="Id">Group number.</param>
/// <param name="FirstIndex">First slot of the group.</param>
/// <param name="LastIndex">Last slot of the group.</param>
/// <param name="HitPointsProperty">The property that is positive while an entity of the group exists.</param>
/// <param name="PositionProperty">The group's column property (the row is the next property).</param>
/// <param name="ThreatPriority">
/// Priority of the group as a threat (also its threat type); an enemy child counts as 1.
/// </param>
public sealed record ObjectGroup(int Id, int FirstIndex, int LastIndex, EntityProperty HitPointsProperty,
    EntityProperty PositionProperty, int ThreatPriority);

/// <summary>Cell categories, target actions and threat search.</summary>
/// <param name="CellCategories">Cell categories by number.</param>
/// <param name="TargetActions">Actions for the kind-independent cell categories.</param>
/// <param name="GatherActionByNodeKind">The action a unit takes when sent to a resource node, by node kind 0..7.</param>
/// <param name="DepositActionByCarriedItem">
/// The unloading action of a unit sent to the store with something, by carried item kind 0..13.
/// </param>
/// <param name="ThreatResponses">Threat responses by threat type.</param>
/// <param name="ObjectGroups">Object groups by number.</param>
/// <param name="ThreatSearchOrder">
/// The object groups searched for threats, in order: row 0 for tribe 0, row 1 for tribe 1, row 2
/// for tribe 1 in mission 3 (tribe 0 in mission 3 skips the second group of row 0).
/// </param>
/// <param name="DepletionStageTenths">
/// A node's depletion stage is the first stage s with amount &lt;= full amount * DepletionStageTenths[s] / 10.
/// </param>
public sealed record TargetTables(IReadOnlyList<CellCategory> CellCategories, TargetActions TargetActions,
    IReadOnlyList<int> GatherActionByNodeKind, IReadOnlyList<int> DepositActionByCarriedItem,
    IReadOnlyList<ThreatResponse> ThreatResponses, IReadOnlyList<ObjectGroup> ObjectGroups,
    IReadOnlyList<IReadOnlyList<int>> ThreatSearchOrder, IReadOnlyList<int> DepletionStageTenths);
