using System.Linq;
using Tribes.Core.Systems.Units.Work;
using Tribes.Legacy;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.LegacyUnits;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="WorkEffects"/> (and <see cref="EntitySlots"/>) with the original.</summary>
public sealed class WorkEffectsTests
{
    private const int ActionCount = 112, ActionsPerUnit = 4;

    private readonly ITestOutputHelper output;

    public WorkEffectsTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Apply_MatchesTheOriginal()
    {
        var run = new UnitWorkParity();
        int[] actions = Enumerable.Range(0, ActionCount).ToArray();
        foreach (LegacySample sample in RichSamples.AllStates)
        {
            foreach (int id in Some(sample, 6))
            {
                foreach (int action in SampleInputs.Pick(actions, (id * 7) + sample.Frame, ActionsPerUnit).Append(Get(sample.Game, ActionField, id)))
                {
                    int a = action;
                    run.Check(sample, $"Apply unit {id} action {a}", g => SetActionLikeTheOriginal(g, id, a),
                        g => { BeginTurn(g, id); g.world_a.J_(); return (g.world_a.i_R, g.world_a.i_A); },
                        s => { WorkStep step = s.WorkEffects.Apply(s.World.Unit(id)); return (step.EffectValue, step.Direction); });
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 500);
    }

    [Fact]
    public void WorkStep_ReachedComparesFromItsSide()
    {
        Assert.True(new WorkStep(5, 1).Reached(5));
        Assert.False(new WorkStep(4, 1).Reached(5));
        Assert.True(new WorkStep(-1, -1).Reached(0));
        Assert.False(new WorkStep(1, -1).Reached(0));
    }

    [Fact]
    public void EntitySlots_ReadNothingFromSlotsOfNoEntity()
    {
        var world = TestWorlds.Empty();
        Assert.Equal(0, EntitySlots.Read(world, Tribes.Core.World.EntityProperty.UnitHitPoints, 0));
        Assert.Equal(0, EntitySlots.Read(world, Tribes.Core.World.EntityProperty.BuildingHitPoints, 61));
        Assert.Equal(0, EntitySlots.Read(world, Tribes.Core.World.EntityProperty.AnimalHitPoints, -1));
        Assert.Equal(1, EntitySlots.Read(world, Tribes.Core.World.EntityProperty.UnitConstantOne, 500));
        EntitySlots.Add(world, Tribes.Core.World.EntityProperty.UnitHitPoints, 0, 5);
        EntitySlots.Write(world, Tribes.Core.World.EntityProperty.NodeAmount, 99, 5);
        world.Unit(3).HitPoints = 7;
        EntitySlots.Add(world, Tribes.Core.World.EntityProperty.UnitHitPoints, 3, 5);
        Assert.Equal(12, world.Unit(3).HitPoints);
        Assert.False(EntitySlots.NearChief(world, world.Unit(3), 4));
    }

    /// <summary>Gives unit <paramref name="id"/> <paramref name="action"/> as the original's action switch does (the effect object follows).</summary>
    private static void SetActionLikeTheOriginal(GameCanvas g, int id, int action)
    {
        g.world_a.i_v = id;
        g.world_a.a_IB(id, (sbyte)action);
    }
}
