using System.Linq;
using Tribes.Core.Content;
using Tribes.Core.Simulation;
using Tribes.Core.Systems.Search;
using Tribes.Core.World;
using Tribes.Parity;
using Xunit;
using Xunit.Abstractions;
using static Tribes.Core.Tests.Systems.WorldServiceInputs;

namespace Tribes.Core.Tests.Systems;

/// <summary>Parity of <see cref="NearbySearches"/> with the original's spiral search modes 0, 3, 4, 8 and 9.</summary>
public sealed class NearbySearchesTests
{
    private const int MoreWorkMode = 0, CoupleMode = 3, GroundMode = 4, PlayerAdultsMode = 8, ComputerAdultsMode = 9;
    private const int UnitsPerSample = 4, CoupleField = 82;

    private readonly ITestOutputHelper output;

    public NearbySearchesTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void SendToMoreWorkAndGround_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates.Concat(MissionSamples.All.Where((_, i) => i % 4 == 0)))
        {
            var game = sample.Restore();
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game).Where(u => Outdoors(game, u)), UnitsPerSample))
            {
                int range = Range(game, id);
                GridPoint at = UnitCell(game, id);
                int tribe = TribeOf(id);
                foreach (int rounds in new[] { 7, 19 })
                {
                    run.Check(sample, $"SendToMoreWork unit {id} {rounds}",
                        g => Original(g, id, tribe, range, () => g.world_a.a_IIII_Z(MoreWorkMode, rounds, at.X, at.Y)),
                        s => s.NearbySearches.SendToMoreWork(s.World.Unit(id), at, rounds));
                }
                run.Check(sample, $"SendToGround unit {id}",
                    g => Original(g, id, tribe, range, () => g.world_a.a_IIII_Z(GroundMode, 9, at.X, at.Y)),
                    s => s.NearbySearches.SendToGround(s.World.Unit(id), at, 9));
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 300);
    }

    [Fact]
    public void PlaceCoupleAndCollectAdults_MatchTheOriginal()
    {
        var run = new ParityRun();
        foreach (LegacySample sample in RichSamples.AllStates.Concat(MissionSamples.All.Where((_, i) => i % 3 == 0)))
        {
            var game = sample.Restore();
            for (int couple = 0; couple < 10 && game.world_a.bArr2_c[0][couple] != 0; couple++)
            {
                int slot = couple;
                foreach (int id in new[] { (int)game.world_a.bArr2_c[0][slot], game.world_a.bArr2_c[1][slot] }.Where(u => u > 0))
                {
                    run.Check(sample, $"PlaceCouple {slot} unit {id}",
                        g => { g.world_a.i_v = id; g.world_a.i_ae = slot; return g.world_a.a_IIII_Z(CoupleMode, 127, g.world_a.bArr2_c[2][slot], g.world_a.bArr2_c[3][slot]); },
                        s => s.NearbySearches.PlaceCouple(s.World.Unit(id), s.World.Couples[slot]));
                }
            }
            foreach (int id in SampleInputs.Spread(LegacySamples.LiveUnits(game), 2))
            {
                GridPoint at = UnitCell(game, id);
                foreach (int mode in new[] { PlayerAdultsMode, ComputerAdultsMode })
                {
                    int tribe = mode == PlayerAdultsMode ? 0 : 1;
                    run.Check(sample, $"CollectAdults {mode} around {id}",
                        g => { g.world_a.i_O = 0; g.world_a.a_IIII_Z(mode, 25, at.X, at.Y); return g.world_a.i_O; },
                        s => s.NearbySearches.CollectAdults(at, 25, tribe));
                }
            }
        }
        output.WriteLine(run.ToString());
        run.AssertAgrees(minimum: 100);
    }

    [Fact]
    public void CollectAdults_StopsAtTheLastActorPosition()
    {
        GameWorld world = TestWorlds.Empty();
        var systems = new RuleSystems(world);
        for (int id = 1; id <= 50; id++)
        {
            Unit unit = world.Unit(id);
            unit.HitPoints = 10;
            unit.Age = 20;
            unit.X = 1 + (id % 10);
            unit.Y = 1 + (id / 10);
            world.Map.SetCell(new GridPoint(unit.X, unit.Y), Maps.MapCell.UnitAt(id));
        }
        int added = systems.NearbySearches.CollectAdults(new GridPoint(5, 3), 25, 0);
        Assert.Equal(ScriptState.ActorSlots - 6, added);
    }

    /// <summary>Whether unit <paramref name="id"/> stands on its own cell (the searches are made for units outdoors).</summary>
    private static bool Outdoors(Tribes.Legacy.GameCanvas game, int id)
    {
        GridPoint at = UnitCell(game, id);
        return CodeAt(game, at.X, at.Y) == id;
    }

    /// <summary>Runs <paramref name="search"/> on the original for unit <paramref name="id"/> with its registers set.</summary>
    private static bool Original(Tribes.Legacy.GameCanvas game, int id, int tribe, int range, System.Func<bool> search)
    {
        game.world_a.i_v = id;
        game.world_a.i_u = tribe;
        game.world_a.i_B = range;
        return search();
    }

    /// <summary>The reach of the unit's current action (the original's i_B for the unit).</summary>
    private static int Range(Tribes.Legacy.GameCanvas game, int id)
    {
        GameWorld world = LegacyRules.Import(game, LegacySamples.Content);
        return new RuleSystems(world).Routes.ActionRange(world.Unit(id));
    }
}
