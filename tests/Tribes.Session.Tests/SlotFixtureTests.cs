using System;
using System.IO;
using System.Linq;
using Tribes.Core.Simulation;
using Tribes.Core.World;
using Tribes.Net;
using Tribes.Net.Transport.Loopback;
using Tribes.Session.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Session.Tests;

/// <summary>
/// Matches and single-player custom games saved by builds that gave every tribe the original's
/// fixed slot blocks (world formats before 5) go on without limits once loaded: a tribe joining a
/// match gets only the slots it uses, after every slot there is, and the world saves and loads
/// again in the current format. Campaign games keep the original's limits.
/// </summary>
public sealed class SlotFixtureTests(ITestOutputHelper output)
{
    private static readonly Guid FixtureHostKey = new("5a7e5a7e-0000-4000-8000-000000000001");

    private static string Folder => Path.Combine(TestContent.RepoRoot(), "tests", "Tribes.Session.Tests", "Fixtures");

    [Fact]
    public void OlderMatchSaves_GoOnWithSlotsThatGrow()
    {
        int matches = 0;
        foreach (string file in Directory.GetFiles(Folder, "*.save").OrderBy(f => f, StringComparer.Ordinal))
        {
            byte[] data = File.ReadAllBytes(file);
            SaveHeader header = MatchSaveFile.ReadHeader(data);
            if (header.Kind == SaveKind.SinglePlayer)
            {
                using LocalMatchRunner runner = LocalMatchRunner.Load(TestContent.Content, data, TestContent.Runner());
                Assert.Equal(header.Match != MatchKind.Campaign, runner.Simulation.World.GrowsOnDemand);
                matches += Check(Path.GetFileName(file), runner.Simulation) ? 1 : 0;
            }
            else
            {
                using var cluster = new SessionCluster(MultiTribeControl.Instance, seed: 81);
                cluster.Host.Close();
                cluster.Host.Dispose();
                HostOptions options = cluster.HostOptions(cluster.HostPeer) with { PlayerId = FixtureHostKey };
                NetworkMatchRunner runner = NetworkMatchRunner.Resume(new LoopbackTransport(cluster.Network, SessionCluster.HostAddress), options, data,
                    cluster.Factory, cluster.RunnerOptions(cluster.HostPeer), cluster.Clock);
                matches += Check(Path.GetFileName(file), runner.Simulation) ? 1 : 0;
            }
        }
        Assert.True(matches >= 6, $"{matches} saves without limits among the fixtures");
    }

    private bool Check(string file, GameSimulation simulation)
    {
        GameWorld world = simulation.World;
        if (!world.GrowsOnDemand)
        {
            return false;
        }
        Assert.Null(world.UnitLimit);
        Assert.Null(world.BuildingLimit);
        if (world.Mode != WorldMode.Match)
        {
            AssertPlaysOnAndReloads(simulation);
            return true;
        }
        Assert.Equal((0, 0), (world.Layout.UnitsPerTribe, world.Layout.BuildingsPerTribe));
        int units = world.UnitSlots, buildings = world.BuildingSlots;
        int tribe = simulation.AddTribe(TribeController.Computer());
        output.WriteLine($"{file}: {world.Tribes.Count} tribes, slots {units}/{buildings}, joined tribe {tribe}");
        if (tribe >= 0)
        {
            Assert.All(world.UnitsOf(tribe), u => Assert.True(u.Id > units));
            Assert.All(world.BuildingsOf(tribe), b => Assert.True(b.Id > buildings));
            Assert.True(world.UnitsOf(tribe).Count > 0 && world.UnitsOf(tribe).All(u => u.HitPoints != 0), "the joined tribe has only the unit slots it uses");
            Assert.True(world.BuildingsOf(tribe).Count > 0 && world.BuildingsOf(tribe).All(b => b.HitPoints != 0), "and only the building slots");
        }
        AssertPlaysOnAndReloads(simulation);
        return true;
    }

    /// <summary>
    /// A single-player world saved by a build of the current world format is written back byte for
    /// byte by this build, and saving what that loads gives the same bytes again.
    /// </summary>
    [Fact]
    public void CurrentFormatFixtures_SaveBackTheSameBytes()
    {
        int checkedWorlds = 0;
        foreach (string file in Directory.GetFiles(Folder, "*.save").OrderBy(f => f, StringComparer.Ordinal))
        {
            (SaveHeader header, byte[] body) = MatchSaveFile.Read(File.ReadAllBytes(file));
            if (header.Kind != SaveKind.SinglePlayer)
            {
                continue;
            }
            byte[] saved = GameSimulation.Load(TestContent.Content, body).Save();
            byte[] again = GameSimulation.Load(TestContent.Content, saved).Save();
            Assert.Equal(saved, again);
            if (WorldFormatOf(body) == StateExchange.CurrentFormat)
            {
                Assert.Equal(body, saved);
                checkedWorlds++;
            }
            output.WriteLine($"{Path.GetFileName(file)}: world format {WorldFormatOf(body)}, {saved.Length} bytes");
        }
        Assert.True(checkedWorlds >= 2, $"{checkedWorlds} single-player fixtures of world format {StateExchange.CurrentFormat}");
    }

    /// <summary>A world saved before tribes had a quiet time of their own gives every tribe the world's.</summary>
    [Fact]
    public void OlderSaves_GiveEveryTribeTheWorldsQuietTime()
    {
        int older = 0;
        foreach (string file in Directory.GetFiles(Folder, "*.save").OrderBy(f => f, StringComparer.Ordinal))
        {
            (SaveHeader header, byte[] body) = MatchSaveFile.Read(File.ReadAllBytes(file));
            if (header.Kind != SaveKind.SinglePlayer || WorldFormatOf(body) >= StateExchange.CombatFormat)
            {
                continue;
            }
            GameWorld world = GameSimulation.Load(TestContent.Content, body).World;
            Assert.All(world.Tribes, tribe => Assert.Equal(world.FramesSinceCombat, tribe.FramesSinceCombat));
            older++;
        }
        Assert.True(older >= 4, $"{older} older single-player fixtures");
    }

    /// <summary>The world snapshot format inside a <see cref="GameSimulation"/> save (the word after the snapshot's magic).</summary>
    private static int WorldFormatOf(byte[] body)
    {
        byte[] magic = BitConverter.GetBytes(0x57425254);
        for (int i = 0; i + 8 <= body.Length; i++)
        {
            if (body.AsSpan(i, 4).SequenceEqual(magic))
            {
                return BitConverter.ToInt32(body, i + 4);
            }
        }
        return -1;
    }

    [Fact]
    public void CustomGames_HaveNoLimits_TheCampaignKeepsThem()
    {
        using LocalMatchRunner custom = LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup { Match = MatchKind.Custom, Mission = 9 }, TestContent.Runner());
        using LocalMatchRunner campaign = LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup { Match = MatchKind.Campaign, Mission = 2 }, TestContent.Runner());

        Assert.True(custom.Simulation.World.GrowsOnDemand);
        Assert.Null(custom.Simulation.World.BuildingLimit);
        Assert.False(campaign.Simulation.World.GrowsOnDemand);
        Assert.Equal((50, 30), (campaign.Simulation.World.UnitLimit, campaign.Simulation.World.BuildingLimit));
        AssertPlaysOnAndReloads(custom.Simulation);
    }

    private static void AssertPlaysOnAndReloads(GameSimulation simulation)
    {
        for (int tick = 0; tick < 50; tick++)
        {
            simulation.Step();
        }
        GameSimulation loaded = GameSimulation.Load(TestContent.Content, simulation.Save());
        Assert.Equal(simulation.StateHash(), loaded.StateHash());
        Assert.True(loaded.World.GrowsOnDemand);
    }
}
