using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Maps;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Resources;
using Tribes.Core.World;
using Tribes.Legacy;
using Tribes.Trace;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="NodeUpkeep"/> (resource nodes over time) with the original, plus edge cases.</summary>
public sealed class NodeUpkeepTests
{
    private const int NodeKindField = 2, NodeAmountField = 3, NodeCount = 50;

    private readonly ITestOutputHelper output;

    public NodeUpkeepTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void CheckDepletedAndStopGatherers_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in LegacySamples.All.Where((_, i) => i % 2 == 0).Concat(WildlifeSamples.All.Where((_, i) => i % 8 == 0)).Concat(MissionSamples.All.Where((_, i) => i % 3 == 0)))
        {
            LegacySample emptied = WithEmptiedNodes(sample);
            GameCanvas game = emptied.Restore();
            foreach (int node in SampleInputs.Spread(Enumerable.Range(0, NodeCount).Where(n => NodeField(game, NodeKindField, n) != 0), 4))
            {
                run.Check(emptied, $"CheckDepleted node {node}",
                    g => { g.world_a.i_aM = node; g.world_a.aj_(); },
                    s => s.NodeUpkeep.CheckDepleted(s.World.Map.ResourceNodes[node]));
                run.Check(emptied, $"StopGatherers node {node}",
                    g => { g.world_a.i_aM = node; g.world_a.am_(); },
                    s => s.NodeUpkeep.StopGatherers(s.World.Map.ResourceNodes[node]));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 200);
    }

    [Fact]
    public void Update_CarcassRotsTurnsToBonesAndBonesVanish()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        ResourceNode carcass = systems.WildlifeAndNodes.PlaceNode(6, new GridPoint(4, 4), 1)!;
        world.Frame = NodeUpkeep.RotPeriodMask + 2;
        systems.NodeUpkeep.Update(carcass);
        Assert.Equal(0, carcass.Amount);
        world.Frame = carcass.Index & NodeUpkeep.DepletionCheckMask;
        systems.NodeUpkeep.Update(carcass);
        Assert.Equal(7, carcass.Kind);
        Assert.Equal(world.Content.ResourceNodeKinds[7].MaxAmount, carcass.Amount);
        Assert.Equal(3, carcass.Stage);
        carcass.Amount = 0;
        systems.NodeUpkeep.Update(carcass);
        Assert.False(carcass.InUse);
        Assert.True(world.Map.CellAt(new GridPoint(4, 4)).IsEmpty);
    }

    [Fact]
    public void Update_PlantsGrowBackToTheirFullAmount()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        ResourceNode plant = systems.WildlifeAndNodes.PlaceNode(1, new GridPoint(2, 2), 0)!;
        int full = world.Content.ResourceNodeKinds[1].MaxAmount;
        world.Frame = NodeUpkeep.GrowthPeriodMask + 2;
        systems.NodeUpkeep.Update(plant);
        Assert.Equal(1, plant.Amount);
        Assert.Equal(0, plant.Stage);
        plant.Amount = full;
        systems.NodeUpkeep.Update(plant);
        Assert.Equal(full, plant.Amount);
        Assert.Equal(3, plant.Stage);
    }

    /// <summary>The sample with every fourth node (and every carcass or bones node) run out.</summary>
    private static LegacySample WithEmptiedNodes(LegacySample sample)
    {
        GameCanvas game = sample.Restore();
        for (int n = 0; n < NodeCount; n++)
        {
            int kind = NodeField(game, NodeKindField, n);
            if (kind != 0 && (n % 4 == sample.Frame % 4 || kind >= 6))
            {
                game.bArr_e[Parity.EntityFields.NodeBase + (NodeAmountField * Parity.EntityFields.NodeStride) + n] = 0;
            }
        }
        return sample with { Frame = sample.Frame + 2, State = LegacySnapshot.Capture(game) };
    }
}
