using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Content.Rules;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Story;
using Tribes.Core.World;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="StoryTriggers"/> and <see cref="AlarmCalls"/> with the original's story triggers.</summary>
public sealed class StoryTriggersTests
{
    private const int ChiefField = 17, QuietFrames = 50, AmbushMission = 4, EnemyBuildings = 14, EnemyFirstBuildingHp = 8505 + 31;

    private readonly ITestOutputHelper output;

    public StoryTriggersTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void AlarmsAndChief_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            foreach (int chiefHp in new[] { 1, 0, -1 })
            {
                int frame = (sample.Frame % 1000) & ~7;
                run.Check(sample, $"Chief {chiefHp}", g =>
                {
                    ScenarioSamples.QuietOriginal(g, frame);
                    if (chiefHp < 1)
                    {
                        g.bArr_e[Parity.EntityFields.UnitBase + (19 * 101) + g.sArr2_a[0][ChiefField]] = (sbyte)chiefHp;
                    }
                    return g.world_a.t__Z();
                }, s =>
                {
                    ScenarioSamples.Quiet(s.World, frame);
                    if (chiefHp < 1 && s.World.Tribes[0][TribeCounter.Chief] > 0)
                    {
                        s.World.Unit(s.World.Tribes[0][TribeCounter.Chief]).HitPoints = chiefHp;
                    }
                    s.AlarmCalls.Update();
                    return s.StoryTriggers.CheckChief();
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 40);
    }

    [Fact]
    public void CheckObjective_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            int mission = ScenarioSamples.MissionOf(sample);
            int steps = LegacySamples.Content.Rules.Missions.Missions[mission].StorySteps.Count;
            for (int stage = 0; stage < steps; stage++)
            {
                foreach ((int timer, bool beaten) in new[] { (0, false), (1000000, false), (0, true) })
                {
                    int s0 = stage;
                    run.Check(sample, $"Objective {mission}:{stage}:{timer}:{beaten}", g =>
                    {
                        ScenarioSamples.QuietOriginal(g, g.i_c);
                        (g.world_a.b_f, g.world_a.i_F) = ((sbyte)s0, timer == 0 ? 3 : timer);
                        if (beaten)
                        {
                            (g.sArr2_a[1][EnemyBuildings], g.bArr_e[EnemyFirstBuildingHp]) = (0, 0);
                        }
                        return g.world_a.v__Z();
                    }, s =>
                    {
                        ScenarioSamples.Quiet(s.World, s.World.Frame);
                        (s.World.Script.ObjectiveStage, s.World.Script.ObjectiveTimer) = (s0, timer == 0 ? 3 : timer);
                        if (beaten)
                        {
                            s.World.Tribes[1][TribeCounter.Buildings] = 0;
                            s.World.Building(31).HitPoints = 0;
                        }
                        return s.StoryTriggers.CheckObjective();
                    });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 60);
    }

    [Fact]
    public void AreaReached_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 2))
        {
            int mission = ScenarioSamples.MissionOf(sample);
            var steps = LegacySamples.Content.Rules.Missions.Missions[mission].StorySteps;
            int[] units = LegacySamples.LiveUnits(sample.Restore()).ToArray();
            for (int stage = 0; stage < steps.Count && units.Length > 1; stage++)
            {
                CellRect area = steps[stage].Area;
                int s0 = stage, lead = units[0], subject = units[^1];
                run.Check(sample, $"Area {mission}:{stage}", g =>
                {
                    g.world_a.b_f = (sbyte)s0;
                    g.world_a.b_IIII_V(lead, subject, area.Left, area.Top);
                }, s =>
                {
                    s.World.Script.ObjectiveStage = s0;
                    s.StoryTriggers.AreaReached(lead, subject, new GridPoint(area.Left, area.Top));
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 30);
    }

    [Fact]
    public void CheckAmbushScenes_MatchesTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in ScenarioSamples.Played(everyNth: 1).Where(s => ScenarioSamples.MissionOf(s) == AmbushMission))
        {
            foreach ((int stage, int frame, bool seen) in new[] { (0, 100, true), (1, 10000, false), (2, 17000, false), (1, 9000, false) })
            {
                run.Check(sample, $"Ambush {stage}:{frame}:{seen}", g =>
                {
                    ScenarioSamples.QuietOriginal(g, frame);
                    g.world_a.b_f = (sbyte)stage;
                    if (seen)
                    {
                        g.iArr3_a[1][0][67] |= 256;
                    }
                    return g.world_a.u__Z();
                }, s =>
                {
                    ScenarioSamples.Quiet(s.World, frame);
                    s.World.Script.ObjectiveStage = stage;
                    if (seen)
                    {
                        s.World.Fog(0).SetVisible(new GridPoint(8, 67), true);
                    }
                    return s.StoryTriggers.CheckAmbushScenes();
                });
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 20);
    }

    [Fact]
    public void ChiefIsActive_NeedsALivingAdult()
    {
        RuleSystems systems = ScenarioSamples.Started(2);
        GameWorld world = systems.World;
        Assert.True(systems.StoryTriggers.ChiefIsActive());
        world.Tribes[0][TribeCounter.Chief] = 0;
        Assert.False(systems.StoryTriggers.ChiefIsActive());
        world.Tribes[0][TribeCounter.Chief] = 1;
        world.Unit(1).Age = 5;
        Assert.False(systems.StoryTriggers.ChiefIsActive());
        Assert.True(systems.StoryTriggers.CheckChief());
        Assert.True(world.Script.AwaitingNewChief);
        Assert.False(systems.StoryTriggers.CheckChief());
    }
}
