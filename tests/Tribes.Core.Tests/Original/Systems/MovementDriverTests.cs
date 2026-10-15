using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="MovementDriver"/> with the original, plus edge cases.</summary>
public sealed class MovementDriverTests
{
    private readonly ITestOutputHelper output;

    public MovementDriverTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Update_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 6).Where(id => PlaceSwapTests.Walks(game, id)))
            {
                int goal = UnitSampling.GoalDistance(game, id);
                foreach (int far in new[] { 0, 200 })
                {
                    run.Check(sample, $"Update unit {id} goal {goal + far}",
                        g => WaypointsTests.Prepare(g, id, goal + far, 0).u_(),
                        s => s.MovementDriver.Update(WaypointsTests.Turn(s, id, goal + far, 0)));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void Update_AUnitThatStopsWalkingStands()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 32);
        (unit.Animation, unit.AnimationTick, unit.PathLength, unit.StepAndActionTicks) = (0, 3, 4, 0x35);

        systems.MovementDriver.Update(new UnitTurn(unit, 0));

        Assert.Equal((8, 0, 0, 0x05), (unit.AnimationNumber, unit.AnimationTick, unit.PathLength, unit.StepAndActionTicks));
    }

    [Fact]
    public void Update_AUnitWalkingIntoAnEnemyTrapIsCaught()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit unit = UnitNeedsTests.Adult(world, 33);
        (unit.MoveDelay, unit.PathIndex, unit.PathLength, unit.TargetIndex) = (2, 0, 2, -1);
        var trap = new GridPoint(unit.X + 1, unit.Y);
        unit.Path.Set(0, trap.X, trap.Y);
        world.Map.SetCell(trap, new MapCell(CellContent.Trap, 20, 1));
        world.Ai.Flags = 3;

        systems.MovementDriver.Update(new UnitTurn(unit, 200) { RegularDecision = true });
        Assert.Equal((trap.X, CellCode.Trap(1, 20)), (unit.X, unit.NextOccupantOrCellObject));
        systems.MovementDriver.Update(new UnitTurn(unit, 200) { RegularDecision = true });

        Assert.Equal((UnitAnimations.FirstTrapped, 0, SubStates.FirstPose), (unit.AnimationNumber, unit.PathLength, unit.ActionSubState));
        Assert.Equal(2, world.Ai.Flags);
    }
}
