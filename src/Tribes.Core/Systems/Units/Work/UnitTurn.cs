using System;
using Tribes.Core.Systems.Map;
using Tribes.Core.World;

namespace Tribes.Core.Systems.Units.Work;

/// <summary>
/// What the units' update works out for the unit whose turn it is and uses again later in the same
/// turn: how far the unit is from its goal, whether it may take up work or a threat by itself, what
/// the threat search found, whether its action step ended. A turn starts for every unit each frame;
/// the steps of the turn (deciding, working, walking) share one instance.
/// </summary>
internal sealed class UnitTurn
{
    /// <summary>Starts the turn of <paramref name="unit"/>, whose goal is <paramref name="goalDistance"/> away.</summary>
    public UnitTurn(Unit unit, int goalDistance)
    {
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        GoalDistance = goalDistance;
    }

    /// <summary>The unit whose turn it is.</summary>
    public Unit Unit { get; private set; }

    /// <summary>Starts the turn of <paramref name="unit"/> on this instance, as a new turn would start (nothing kept from the last one).</summary>
    public void Restart(Unit unit, int goalDistance)
    {
        Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        GoalDistance = goalDistance;
        MayActFreely = false;
        RegularDecision = false;
        StepEnded = false;
        Threat.Restart(0, 0, 0, 0);
    }

    /// <summary>
    /// The unit's distance to its goal as last measured in this turn, in the movement table's
    /// encoding (10 × cells + facing; 200 beyond 12 cells). <see cref="GoalCells"/> is what the rules compare.
    /// </summary>
    public int GoalDistance { get; set; }

    /// <summary>The distance to the goal in cells.</summary>
    public int GoalCells => GridDistances.Cells(GoalDistance);

    /// <summary>Whether the unit may switch to work or to a threat by itself in the decision under way.</summary>
    public bool MayActFreely { get; set; }

    /// <summary>
    /// Whether the decision under way is the frame's regular one (false while a unit whose target is
    /// gone or whose work is done looks for anything to fight).
    /// </summary>
    public bool RegularDecision { get; set; }

    /// <summary>What the last threat search of this turn found; empty (priority 0) before any search.</summary>
    public TargetSearch Threat { get; set; } = new();

    /// <summary>Whether the unit's current action step ended in this frame (its animation ran out).</summary>
    public bool StepEnded { get; set; }

    /// <summary>Forgets the threat found, as starting a target scan does.</summary>
    public void ClearThreat() => Threat.Priority = 0;
}
