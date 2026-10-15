using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="Council"/> with the original's council scene, plus edge cases.</summary>
public sealed class CouncilTests
{
    private readonly ITestOutputHelper output;

    public CouncilTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Convene_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            foreach (bool witchDoctorFirst in new[] { false, true })
            {
                run.Check(sample, $"Convene {witchDoctorFirst}", g => g.world_a.c_Z(witchDoctorFirst),
                    s => s.Council.Convene(witchDoctorFirst));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 40);
    }

    [Fact]
    public void Dismiss_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            run.Check(sample, "Dismiss", g =>
            {
                g.world_a.c_Z(true);
                g.world_a.f__V();
            }, s =>
            {
                s.Council.Convene(true);
                s.Council.Dismiss();
            });
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 20);
    }

    [Fact]
    public void Dismiss_PutsTheSeatedUnitsBack()
    {
        RuleSystems systems = ScenarioSamples.Started(2);
        GameWorld world = systems.World;
        int chief = world.Tribes[0][TribeCounter.Chief];
        int action = world.Unit(chief).Action;
        systems.Council.Convene(false);
        Assert.True(world.Script.MeetingCount >= 1);
        Assert.Equal(chief, world.Script.Seats[0].Unit);
        systems.Council.Dismiss();
        Assert.Equal(action, world.Unit(chief).Action);
        Assert.NotEqual(0, systems.Council.SeatedOrChief(ScriptState.MeetingSeats - 1));
    }
}
