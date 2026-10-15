using Tribes.Core.Content;
using Tribes.Core.Systems.Resources;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;

namespace Tribes.Core.Simulation;

/// <summary>The last part of the frame: resource nodes, then the story.</summary>
public sealed partial class RuleSystems
{
    private NodeUpkeep? nodeUpkeep;
    private ResourceUpdate? resourceUpdate;

    /// <summary>Resource nodes rotting, growing and running out.</summary>
    internal NodeUpkeep NodeUpkeep => nodeUpkeep ??= new NodeUpkeep(World, UnitActions, TakeNextTarget);

    /// <summary>The nodes' and the story's part of each frame.</summary>
    internal ResourceUpdate ResourceUpdate => resourceUpdate ??= new ResourceUpdate(NodeUpkeep, StoryUpdate);

    /// <summary>A unit outside its own turn (one that stopped gathering) goes on to its next target.</summary>
    private void TakeNextTarget(Unit unit) =>
        TargetCycle.NextTarget(new UnitTurn(unit, TargetDistances.FromUnit(unit, new GridPoint(unit.X, unit.Y)).Measured));
}
