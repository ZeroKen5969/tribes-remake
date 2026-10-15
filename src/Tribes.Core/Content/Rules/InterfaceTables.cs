using System.Collections.Generic;
using Tribes.Core.World;

namespace Tribes.Core.Content.Rules;

/// <summary>A line of the unit info panel: the value shown is the property's value + <see cref="Add"/>.</summary>
/// <param name="Property">The unit property shown, or null for a line without one.</param>
/// <param name="Add">Added to the value.</param>
public sealed record InfoPanelLine(EntityProperty? Property, int Add);

/// <summary>A panel of the user interface.</summary>
/// <param name="Id">Panel number.</param>
/// <param name="Picture">Its rectangle or picture.</param>
/// <param name="FrameStyle">Its frame style (1 or 2).</param>
/// <param name="Layout">Its layout number in the graphics data.</param>
/// <param name="FirstLabel">Its first label slot.</param>
/// <param name="AlwaysRedraw">1 when it is redrawn even when nothing changed.</param>
public sealed record InterfacePanel(int Id, int Picture, int FrameStyle, int Layout, int FirstLabel, int AlwaysRedraw);

/// <summary>Tables only the user interface uses; the rules do not read them.</summary>
/// <param name="SquadListsToPurge">Selection lists a dead unit is removed from (list 4 too).</param>
/// <param name="InfoPanelLines">Lines of the unit info panel, in order.</param>
/// <param name="HitPointBarWidths">Pixel width of each hit point bar picture; a building uses bar MaxHitPoints / 80 + 6.</param>
/// <param name="WeaponSpriteLayers">Picture layer of a unit's weapon, by weapon kind.</param>
/// <param name="HudCounters">The tribe counters shown in the status bar.</param>
/// <param name="UnitPanelProperties">The unit properties shown on the unit panel, in order.</param>
/// <param name="CursorFrame">|d| for d = -1..1, used to draw the cursor frame.</param>
/// <param name="MinimapColors">Minimap colour of each terrain tile.</param>
/// <param name="Panels">User interface panels by number.</param>
/// <param name="CommandBarSecondLabels">
/// The second label of the command bar in each of its 16 modes; meaning unconfirmed (-1 looks like "hidden").
/// </param>
/// <param name="MusicListLengths">Number of tunes in each music list.</param>
/// <param name="HitEffectAnchors">
/// What a floating damage number follows, by cell category: 1 a unit, 2 an animal, others the building's corner.
/// </param>
public sealed record InterfaceTables(IReadOnlyList<int> SquadListsToPurge, IReadOnlyList<InfoPanelLine> InfoPanelLines,
    IReadOnlyList<int> HitPointBarWidths, IReadOnlyList<int> WeaponSpriteLayers, IReadOnlyList<TribeCounter> HudCounters,
    IReadOnlyList<EntityProperty> UnitPanelProperties, IReadOnlyList<int> CursorFrame, IReadOnlyList<int> MinimapColors,
    IReadOnlyList<InterfacePanel> Panels, IReadOnlyList<int> CommandBarSecondLabels, IReadOnlyList<int> MusicListLengths,
    IReadOnlyList<int> HitEffectAnchors);
