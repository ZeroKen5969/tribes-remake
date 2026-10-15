using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="StepMotion"/> with the original, plus edge cases.</summary>
public sealed class StepMotionTests
{
    private const int XField = 0, YField = 1, FacingField = 6, MoveDelayField = 10, TicksField = 11, RunField = 77, PathBlockField = 48;

    private readonly ITestOutputHelper output;

    public StepMotionTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Step_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 6).Where(id => UnitSampling.OnItsCell(game, id)))
            {
                int speed = LegacySamples.Field(game, MoveDelayField, id) - LegacySamples.Field(game, RunField, id);
                if (speed < 2 || speed > 8)
                {
                    continue;
                }
                foreach (int facing in SampleInputs.Pick(Enumerable.Range(0, 8).ToArray(), id + (sample.Frame / 1000), 3))
                {
                    if (!Inside(game, id, facing))
                    {
                        continue;
                    }
                    foreach (int ticks in new[] { 0, 1, 3 })
                    {
                        int swap = ticks == 3 ? StepMotion.Swapping : LegacySamples.Field(game, PathBlockField, id);
                        void Prepare(UnitFields f) => f.Set(FacingField, facing).Set(TicksField, ticks).Set(PathBlockField, swap);
                        run.Check(sample, $"Step unit {id} facing {facing} ticks {ticks} block {swap}",
                            g => { Prepare(new UnitFields(g, id)); UnitSampling.Current(g, id).D_(); },
                            s => { Prepare(new UnitFields(s.World, id)); s.StepMotion.Step(s.World.Unit(id)); });
                    }
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 1000);
    }

    [Fact]
    public void Step_PutsBackTheTrapAUnitStoodOnAndStepsOntoTheNext()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        GameWorld world = systems.World;
        Unit unit = UnitNeedsTests.Adult(world, 16);
        (unit.MoveDelay, unit.Facing, unit.StepAndActionTicks) = (2, 0, 0);
        unit.NextOccupantOrCellObject = CellCode.Trap(1, 20);
        var from = new GridPoint(unit.X, unit.Y);

        systems.StepMotion.Step(unit);

        Assert.Equal(new MapCell(CellContent.Trap, 20, 1), world.Map.CellAt(from));
        Assert.Equal((from.X + 1, 0), (unit.X, unit.NextOccupantOrCellObject));
        Assert.Equal(MapCell.UnitAt(16), world.Map.CellAt(new GridPoint(unit.X, unit.Y)));

        unit.NextOccupantOrCellObject = -CellCode.Trap(1, 21);
        unit.StepAndActionTicks = 0;
        systems.StepMotion.Step(unit);
        Assert.Equal(CellCode.Trap(1, 21), unit.NextOccupantOrCellObject);
        Assert.True(StepMotion.IsCaught(unit));
    }

    /// <summary>Whether the unit's next cell in <paramref name="facing"/> lies inside the map.</summary>
    private static bool Inside(Legacy.GameCanvas game, int id, int facing)
    {
        var d = LegacySamples.Content.Rules.Movement.Directions[facing];
        int x = LegacySamples.Field(game, XField, id) + d.Dx, y = LegacySamples.Field(game, YField, id) + d.Dy;
        return x >= 0 && y >= 0 && x < game.s_c && y < game.s_d;
    }
}
