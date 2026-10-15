using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Arrival"/> with the original, plus edge cases.</summary>
public sealed class ArrivalTests
{
    private const int TargetField = 44, OrderField = 61, KindField = 6;
    private static readonly int[] OrderModes = { 0, 1, 2 };

    private readonly ITestOutputHelper output;

    public ArrivalTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void ArriveAndAcceptsWork_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var buildings = SampleInputs.Spread(LegacySamples.UsedBuildings(game), 6).ToList();
            foreach (int id in UnitSampling.Units(game, 2))
            {
                foreach (int building in SampleInputs.Pick(buildings.ToArray(), id, 3))
                {
                    int kind = LegacySamples.BuildingField(game, KindField, building);
                    int action = LegacySamples.Content.Rules.Buildings.Kinds[kind].Operation?.UseAction ?? ActionNumbers.Idle;
                    int order = OrderModes[(id + building) % OrderModes.Length];
                    void Prepare(UnitFields s) => s.Set(UnitSampling.ActionField, action).Set(TargetField, building).Set(OrderField, order);
                    run.Check(sample, $"Arrive unit {id} building {building} action {action} order {order}",
                        g => { Prepare(new UnitFields(g, id)); UnitSampling.Current(g, id).F_(); },
                        s => { Prepare(new UnitFields(s.World, id)); s.Arrival.Arrive(s.World.Unit(id)); });
                    run.Check(sample, $"AcceptsWork unit {id} building {building} action {action}",
                        g => { Prepare(new UnitFields(g, id)); return UnitSampling.Current(g, id).d_I_Z(building); },
                        s => { Prepare(new UnitFields(s.World, id)); return s.Arrival.AcceptsWork(s.World.Unit(id), s.World.Building(building)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 1000);
    }

    [Fact]
    public void Arrive_AUnitWhoseBuildingIsGoneStaysOutsideAndStops()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 15);
        systems.World.Map.SetCell(new GridPoint(unit.X, unit.Y), MapCell.Empty);
        (unit.TargetIndex, unit.Action, unit.ActionSubState, unit.OffsetX) = (0, 58, SubStates.Entering, 5);

        systems.Arrival.Arrive(unit);

        Assert.Equal((0, SubStates.Normal, ActionNumbers.Idle), (unit.OffsetX, unit.ActionSubState, unit.Action));
        Assert.Equal(MapCell.UnitAt(15), systems.World.Map.CellAt(new GridPoint(unit.X, unit.Y)));
    }
}
