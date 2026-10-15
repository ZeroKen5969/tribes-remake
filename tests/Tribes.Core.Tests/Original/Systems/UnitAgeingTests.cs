using Tribes.Core.Simulation;
using Tribes.Core.Systems.Entities;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitAgeing"/> with the original, plus edge cases.</summary>
public sealed class UnitAgeingTests
{
    private const int UnitsPerTribe = 4;
    private static readonly int[] ForcedAges = { -1, 11, 34, 99 };

    private readonly ITestOutputHelper output;

    public UnitAgeingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Update_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, UnitsPerTribe))
            {
                int year = id <= 50 ? 125 : 50;
                int frame = UnitSampling.FrameWhere(sample.Frame + (id * 5 % 128), 3, id & 3);
                foreach (int age in SampleInputs.Pick(ForcedAges, id + sample.Frame / 1000, 2))
                {
                    int ticks = age < 0 ? LegacySamples.Field(game, UnitSampling.AgeTicksField, id) : year;
                    int setAge = age < 0 ? LegacySamples.Field(game, UnitSampling.AgeField, id) : age;
                    run.Check(sample, $"Update unit {id} age {setAge} ticks {ticks} frame {frame}",
                        g =>
                        {
                            g.i_c = frame;
                            UnitSampling.Set(g, UnitSampling.AgeField, id, setAge);
                            UnitSampling.Set(g, UnitSampling.AgeTicksField, id, ticks);
                            return UnitSampling.Current(g, id).q__Z();
                        },
                        s =>
                        {
                            s.World.Frame = frame;
                            (s.World.Unit(id).Age, s.World.Unit(id).AgeTicks) = (setAge, ticks);
                            return s.UnitAgeing.Update(s.World.Unit(id));
                        });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void Update_AChildComesOfAgeAndStandsWhereItIs()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit child = UnitNeedsTests.Adult(systems.World, 8);
        (child.Age, child.AgeTicks, child.Action, child.ThrowRange, child.SightRadius) = (11, 125, ActionNumbers.ChildIdle, 1, 20);

        Assert.True(systems.UnitAgeing.Update(child));

        Assert.Equal((12, 0, ActionNumbers.Idle, ActionNumbers.Idle), (child.Age, child.AgeTicks, child.Action, child.AssignedTask));
        Assert.Equal((3, 9), (child.ThrowRange, child.SightRadius));
        Assert.Equal((child.X, child.Y), (child.TaskSiteX, child.TaskSiteY));
    }

    [Fact]
    public void Update_TheOldestUnitsDieAndTheAgeTableIsNotOverrun()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit elder = UnitNeedsTests.Adult(systems.World, 9);
        (elder.Age, elder.AgeTicks) = (100, 125);

        Assert.False(systems.UnitAgeing.Update(elder));
        Assert.True(elder.HitPoints < 0);
    }

    [Fact]
    public void Update_TheProtectionSpellWearsOffAndElevenYearOldsStayInside()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 10);
        (unit.Status, unit.ProtectionTicks) = (UnitStatus.Protected, 119);
        Assert.True(systems.UnitAgeing.Update(unit));
        Assert.Equal((UnitStatus.None, 0), (unit.Status, unit.ProtectionTicks));

        Unit child = UnitNeedsTests.Adult(systems.World, 11);
        child.Age = 11;
        systems.World.Map.SetCell(new Content.GridPoint(child.X, child.Y), Maps.MapCell.Empty);
        Assert.True(systems.UnitAgeing.Update(child));
        Assert.Equal((ActionNumbers.StayInside, OrderModes.Forced), (child.Action, child.OrderMode));
    }
}
