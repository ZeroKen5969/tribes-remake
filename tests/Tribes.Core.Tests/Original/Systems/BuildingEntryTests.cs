using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Movement;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="BuildingEntry"/> with the original, plus edge cases.</summary>
public sealed class BuildingEntryTests
{
    private const int XField = 0, YField = 1, AnimationField = 29, DestXField = 31, DestYField = 32, EntranceField = 40, TargetField = 44, KindField = 6;

    private readonly ITestOutputHelper output;

    public BuildingEntryTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void HasRoomAndReachEntrance_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            var buildings = SampleInputs.Spread(LegacySamples.UsedBuildings(game), 6).ToArray();
            foreach (int id in UnitSampling.Units(game, 2).Where(id => UnitSampling.OnItsCell(game, id)))
            {
                foreach (int building in SampleInputs.Pick(buildings, id, 3))
                {
                    int kind = LegacySamples.BuildingField(game, KindField, building);
                    int action = LegacySamples.Content.Rules.Buildings.Kinds[kind].Operation?.UseAction ?? ActionNumbers.Idle;
                    int x = LegacySamples.Field(game, XField, id), y = LegacySamples.Field(game, YField, id);
                    void Prepare(UnitFields f) => f.Set(UnitSampling.ActionField, action).Set(TargetField, building)
                        .Set(EntranceField, 1 + (id % 7)).Set(DestXField, x).Set(DestYField, y).Set(AnimationField, 8);
                    run.Check(sample, $"HasRoom unit {id} building {building} action {action}",
                        g => { Prepare(new UnitFields(g, id)); return UnitSampling.Turn(g, id, 0).c_I(building); },
                        s => { Prepare(new UnitFields(s.World, id)); return s.BuildingEntry.HasRoom(s.World.Unit(id), s.World.Building(building)); });
                    run.Check(sample, $"ReachEntrance unit {id} building {building} action {action}",
                        g => { Prepare(new UnitFields(g, id)); return UnitSampling.Turn(g, id, 0).e__Z(); },
                        s => { Prepare(new UnitFields(s.World, id)); return s.BuildingEntry.ReachEntrance(s.World.Unit(id)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 400);
    }

    [Fact]
    public void ReachEntrance_AWalkingUnitIsBusyAndAnIdleOneIsNot()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 29);
        unit.Animation = 0;
        Assert.True(systems.BuildingEntry.ReachEntrance(unit));
        unit.Animation = 8;
        Assert.False(systems.BuildingEntry.ReachEntrance(unit));
    }
}
