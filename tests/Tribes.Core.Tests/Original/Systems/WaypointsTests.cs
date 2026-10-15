using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.Systems.Units.Work;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Waypoints"/> with the original, plus edge cases.</summary>
public sealed class WaypointsTests
{
    private static readonly int[] Threats = { 0, 3 };

    private readonly ITestOutputHelper output;

    public WaypointsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Next_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 6).Where(id => PlaceSwapTests.Walks(game, id)))
            {
                int goal = UnitSampling.GoalDistance(game, id);
                foreach (int threat in Threats)
                {
                    run.Check(sample, $"Next unit {id} threat {threat}",
                        g =>
                        {
                            Legacy.World w = Prepare(g, id, goal, threat);
                            bool stopped = w.c__Z();
                            return (stopped, w.i_E);
                        },
                        s =>
                        {
                            WaypointStep step = s.Waypoints.Next(Turn(s, id, goal, threat));
                            return (step.Stopped, (int)step.Swap);
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void Next_DropsARouteWhoseWaypointIsNotNextToTheUnit()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 24);
        (unit.PathIndex, unit.PathLength) = (0, 3);
        unit.Path.Set(0, unit.X + 5, unit.Y);
        var turn = new UnitTurn(unit, 0) { RegularDecision = true };

        Assert.True(systems.Waypoints.Next(turn).Stopped);
        Assert.Equal(0, unit.PathLength);
    }

    [Fact]
    public void Next_ReservesTheCellAheadAndKeepsTheTrapOnIt()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 25);
        (unit.PathIndex, unit.PathLength, unit.Animation) = (0, 3, 0);
        var ahead = new GridPoint(unit.X, unit.Y + 1);
        unit.Path.Set(0, ahead.X, ahead.Y);
        systems.World.Map.SetCell(ahead, new MapCell(CellContent.Trap, 20, 1));

        WaypointStep step = systems.Waypoints.Next(new UnitTurn(unit, 0) { RegularDecision = true });

        Assert.Equal(new WaypointStep(false, SwapResult.None), step);
        Assert.Equal((2, 1, -CellCode.Trap(1, 20)), (unit.Facing, unit.PathIndex, unit.NextOccupantOrCellObject));
        Assert.Equal(MapCell.Of(CellContent.Reserved), systems.World.Map.CellAt(ahead));
    }

    internal static Legacy.World Prepare(Legacy.GameCanvas g, int id, int goal, int threat)
    {
        Legacy.World w = UnitSampling.Turn(g, id, goal);
        (w.z_b, w.b_y, w.i_E) = (false, (sbyte)threat, 0);
        return w;
    }

    internal static UnitTurn Turn(RuleSystems s, int id, int goal, int threat)
    {
        var turn = new UnitTurn(s.World.Unit(id), goal) { RegularDecision = true };
        turn.Threat.Priority = threat;
        return turn;
    }
}
