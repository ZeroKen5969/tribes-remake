using System.Collections.Generic;

namespace Tribes.Core.Content.Rules;

/// <summary>
/// One of the eight facings: 0 east, then clockwise (1 south-east, 2 south ... 7 north-east); rows
/// grow downwards, odd facings are diagonals.
/// </summary>
/// <param name="Facing">The facing, 0..7.</param>
/// <param name="Dx">Column step of one cell in this facing.</param>
/// <param name="Dy">Row step of one cell in this facing.</param>
/// <param name="SideDx">
/// Column step of the facing turned a quarter anticlockwise; two tribe mates swapping cells slide
/// sideways along it.
/// </param>
/// <param name="SideDy">Row step of the facing turned a quarter anticlockwise.</param>
public sealed record DirectionStep(int Facing, int Dx, int Dy, int SideDx, int SideDy);

/// <summary>
/// The two steps a straight line is drawn with in one octant, for estimating how much a direct
/// route between two cells is blocked.
/// </summary>
/// <param name="Octant">
/// (dx &gt; 0 ? 4 : 0) + (dy &gt; 0 ? 2 : 0) + (|dx| &gt; |dy| ? 1 : 0) for the line's delta.
/// </param>
/// <param name="Straight">The straight step, taken |long axis| - |short axis| times.</param>
/// <param name="Diagonal">The diagonal step, taken |short axis| times.</param>
public sealed record LineStepPattern(int Octant, CellOffset Straight, CellOffset Diagonal);

/// <summary>Movement of one speed class in straight or diagonal steps.</summary>
/// <param name="StepFrames">
/// Frames one cell takes; the mover enters the next cell at frame (StepFrames + 1) / 2. Zero for
/// the classes nothing moves at.
/// </param>
/// <param name="OffsetX">Pixel offset from the cell centre at each frame of the step, columns (a cell is 22 wide).</param>
/// <param name="OffsetY">Pixel offset at each frame of the step, rows (a cell is 16 high).</param>
/// <param name="SwapOffsetX">
/// Sideways pixel offset at each frame while two tribe mates swap cells, multiplied by the
/// facing's <see cref="DirectionStep.SideDx"/>.
/// </param>
/// <param name="SwapOffsetY">Sideways pixel offset while swapping, multiplied by <see cref="DirectionStep.SideDy"/>.</param>
/// <param name="RunStartTicks">
/// When a unit whose walking speed class is this one starts running, its frame within the step t
/// becomes RunStartTicks[t]; empty for classes without a running speed.
/// </param>
/// <param name="RunStopTicks">
/// When such a unit stops running, its frame within the running step t becomes RunStopTicks[t].
/// </param>
public sealed record SpeedGait(int StepFrames, IReadOnlyList<int> OffsetX, IReadOnlyList<int> OffsetY,
    IReadOnlyList<int> SwapOffsetX, IReadOnlyList<int> SwapOffsetY, IReadOnlyList<int> RunStartTicks,
    IReadOnlyList<int> RunStopTicks);

/// <summary>
/// A speed class: a unit's class is <c>MoveDelay - RunBonus</c>, an animal's comes from its kind.
/// Smaller is faster; only classes 2..8 move (0 and 1 have no frames).
/// </summary>
/// <param name="Id">The class.</param>
/// <param name="Straight">Movement along a row or column.</param>
/// <param name="Diagonal">Movement along a diagonal.</param>
public sealed record SpeedClass(int Id, SpeedGait Straight, SpeedGait Diagonal);

/// <summary>The cells a sight radius reveals: rows of a disc around the viewer.</summary>
/// <param name="Radius">The radius.</param>
/// <param name="RowWidths">Width of each of the 2 * Radius + 1 rows, from the top row down, centred on the viewer.</param>
public sealed record RevealDisc(int Radius, IReadOnlyList<int> RowWidths);

/// <summary>Direction, line and distance tables used for moving and measuring on the grid.</summary>
/// <param name="Directions">The eight facings, by facing.</param>
/// <param name="FacingByDeltaRowMajor">
/// Facing of a one-cell delta: index (dx + 1) + (dy + 1) * 3; the zero delta gives 0.
/// </param>
/// <param name="FacingByDeltaColumnMajor">
/// The same map indexed (dx + 1) * 3 + (dy + 1), as units and animals turning towards a waypoint use it.
/// </param>
/// <param name="LineSteps">Line drawing steps, by octant.</param>
/// <param name="SpeedClasses">Speed classes 0..8, by class.</param>
/// <param name="CellDistances">
/// Distance between cells about 22 * sqrt(dx² + dy²) (a cell is 22 pixels wide), as rows by |dy|
/// 0..10 of columns by |dx| 0..10; used to time thrown projectiles.
/// </param>
/// <param name="DistanceAndFacing">
/// 25 rows by dy + 12 of 25 columns by dx + 12 (dx, dy in -12..12): 10 * grid distance + the
/// facing from the centre towards the cell. The distance is a hand-tuned eight-way estimate;
/// far corners hold 100.
/// </param>
/// <param name="RevealDiscs">Discs a sight radius reveals, by radius.</param>
public sealed record MovementTables(IReadOnlyList<DirectionStep> Directions,
    IReadOnlyList<int> FacingByDeltaRowMajor, IReadOnlyList<int> FacingByDeltaColumnMajor,
    IReadOnlyList<LineStepPattern> LineSteps, IReadOnlyList<SpeedClass> SpeedClasses,
    IReadOnlyList<IReadOnlyList<int>> CellDistances, IReadOnlyList<IReadOnlyList<int>> DistanceAndFacing,
    IReadOnlyList<RevealDisc> RevealDiscs);
