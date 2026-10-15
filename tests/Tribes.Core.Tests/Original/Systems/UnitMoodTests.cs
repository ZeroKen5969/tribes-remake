using Tribes.Core.Simulation;
using Tribes.Core.Systems.Units.Body;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="UnitMood"/> with the original, plus edge cases.</summary>
public sealed class UnitMoodTests
{
    private const int HappinessField = 5, EnergyField = 21;

    private readonly ITestOutputHelper output;

    public UnitMoodTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Update_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            var game = sample.Restore();
            foreach (int id in UnitSampling.Units(game, 5))
            {
                foreach (int energy in new[] { -1, 10 })
                {
                    int setEnergy = energy < 0 ? LegacySamples.Field(game, EnergyField, id) : energy;
                    run.Check(sample, $"Update unit {id} energy {setEnergy}",
                        g => { UnitSampling.Set(g, EnergyField, id, setEnergy); UnitSampling.Current(g, id).W_(); },
                        s => { s.World.Unit(id).Energy = setEnergy; s.UnitMood.Update(s.World.Unit(id)); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void Update_WaitsForTheNeedsToStartAndStaysWithinRange()
    {
        var systems = new RuleSystems(TestWorlds.Empty());
        Unit unit = UnitNeedsTests.Adult(systems.World, 12);
        unit.Happiness = 98;
        systems.UnitMood.Update(unit);
        Assert.Equal(98, unit.Happiness);

        systems.World.Mission = 1;
        (unit.Happiness, unit.Home, unit.Energy, unit.Satiety) = (1, 0, 5, 5);
        unit.Action = 0;
        systems.UnitMood.Update(unit);
        Assert.Equal(0, unit.Happiness);
        Assert.Equal(HappinessField, (int)EntityProperty.UnitHappiness);
    }
}
