using System;
using System.IO;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Transport.Loopback;
using Tribes.Session.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Tribes.Session.Tests;

/// <summary>
/// Saves written by earlier builds must load and play on this build. They are kept in the content
/// repository (data/fixtures/saves/*.save): a save holds its map, and the maps are the original's
/// (the endless worlds are pieced together from them). When a save format changes, the old
/// fixtures stay and new ones are added (TRIBES_WRITE_FIXTURES=1 writes this build's as the next
/// numbered ones).
/// </summary>
public sealed class SaveFixtureTests(ITestOutputHelper output)
{
    /// <summary>The player key every hosted fixture was saved with (only its host may resume it).</summary>
    private static readonly Guid FixtureHostKey = new("5a7e5a7e-0000-4000-8000-000000000001");

    private const int PlayTicks = 200;

    private static string Folder => Path.Combine(TestContent.RepoRoot(), "data", "fixtures", "saves");

    [Fact]
    public void EveryFixture_LoadsAndPlaysOn()
    {
        if (Environment.GetEnvironmentVariable("TRIBES_WRITE_FIXTURES") == "1")
        {
            WriteFixtures();
        }
        string[] files = [.. Directory.GetFiles(Folder, "*.save").OrderBy(f => f, StringComparer.Ordinal)];
        Assert.Contains(files, f => MatchSaveFile.ReadHeader(File.ReadAllBytes(f)).Kind == SaveKind.SinglePlayer);
        Assert.Contains(files, f => MatchSaveFile.ReadHeader(File.ReadAllBytes(f)).Kind == SaveKind.HostedMatch);
        foreach (string file in files)
        {
            byte[] data = File.ReadAllBytes(file);
            SaveHeader header = MatchSaveFile.ReadHeader(data);
            long played = header.Kind == SaveKind.SinglePlayer ? PlaySingle(data) : PlayHosted(data);
            output.WriteLine($"{Path.GetFileName(file)}: {header.Kind} {header.Match} mission {header.Mission} by {header.GameVersion} ({header.ContentVersion}), tick {header.Tick} -> {played}");
            Assert.True(played >= header.Tick + PlayTicks, $"{Path.GetFileName(file)} stopped at tick {played}");
        }
    }

    private static long PlaySingle(byte[] data)
    {
        using LocalMatchRunner runner = LocalMatchRunner.Load(TestContent.Content, data, TestContent.Runner());
        var script = new ScriptedOrders(5);
        for (int i = 0; i < PlayTicks; i++)
        {
            if (runner.Orders.CanSubmit && script.Next(runner.Simulation, runner.LocalTribe) is { } order)
            {
                runner.Orders.Submit(order);
            }
            runner.StepOnce();
        }
        Assert.NotEqual(MatchPhase.Stopped, runner.Phase);
        return runner.Simulation.Tick;
    }

    private static long PlayHosted(byte[] data)
    {
        using var cluster = new SessionCluster(MultiTribeControl.Instance, seed: 81);
        cluster.Host.Close();
        cluster.Host.Dispose();
        HostOptions options = cluster.HostOptions(cluster.HostPeer) with { PlayerId = FixtureHostKey };
        NetworkMatchRunner runner = NetworkMatchRunner.Resume(new LoopbackTransport(cluster.Network, SessionCluster.HostAddress), options, data,
            cluster.Factory, cluster.RunnerOptions(cluster.HostPeer), cluster.Clock);
        cluster.AdoptHost(runner, FixtureHostKey);
        Assert.Equal(RequestResult.Accepted, runner.ContinueResumed());
        runner.SetSpeed(400);
        cluster.RunTicks(PlayTicks);
        Assert.NotEqual(MatchPhase.Stopped, runner.Phase);
        return runner.Simulation.Tick;
    }

    /// <summary>
    /// Writes a single-player and a hosted save of this build, of a custom map and of an endless
    /// world each, as the next numbered fixtures.
    /// </summary>
    private static void WriteFixtures()
    {
        Directory.CreateDirectory(Folder);
        int next = 1 + Directory.GetFiles(Folder, "*.save").Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f).Split('-')[^1], out int n) ? n : 0).DefaultIfEmpty(0).Max();
        WriteSingle(new LocalMatchSetup { Match = MatchKind.Custom, Mission = 8, Difficulty = 0, Seed = 17, PlayerName = "Fixture", Colour = 2, ComputerColour = 5 },
            "custom-1", $"single-player-{next}.save");
        WriteSingle(new LocalMatchSetup { Match = MatchKind.Endless, Difficulty = 0, Seed = 17, PlayerName = "Fixture", Colour = 2, ComputerTribes = 3, NewTribes = 3, Settings = FixtureSettings },
            "endless-1", $"endless-single-player-{next}.save");
        WriteHosted(new MatchPlan { Mission = 9, Difficulty = 1, Seed = 17, Settings = FixtureSettings }, $"hosted-match-{next}.save");
        WriteHosted(new MatchPlan { Mission = -1, Difficulty = 0, Seed = 17, Endless = new EndlessPlan(17, 0, 3, 3) }, $"endless-hosted-match-{next}.save");
    }

    /// <summary>Settings other than the defaults, so the fixtures hold them.</summary>
    private static readonly Tribes.Core.World.WorldSettings FixtureSettings = new() { OpeningGraceFrames = 1500, StartStockPercent = 200, WildAnimals = false };

    private static void WriteSingle(LocalMatchSetup setup, string stem, string file)
    {
        var store = new MemorySaveStore();
        using (LocalMatchRunner single = LocalMatchRunner.Start(TestContent.Content, setup, TestContent.Runner(store, 0)))
        {
            var script = new ScriptedOrders(17);
            for (int i = 0; i < 300; i++)
            {
                if (script.Next(single.Simulation, single.LocalTribe) is { } order)
                {
                    single.Orders.Submit(order);
                }
                single.StepOnce();
            }
            single.Save("1");
        }
        File.WriteAllBytes(Path.Combine(Folder, file), store.Files[stem]);
    }

    private static void WriteHosted(MatchPlan plan, string file)
    {
        using var cluster = new SessionCluster(MultiTribeControl.Instance, seed: 17);
        cluster.Host.Close();
        cluster.Host.Dispose();
        var host = new HostSession(new LoopbackTransport(cluster.Network, "fixture-host"), cluster.HostOptions(cluster.HostPeer) with { PlayerId = FixtureHostKey },
            cluster.Factory, cluster.Clock);
        host.AddComputerPlayer(aiLevel: 0);
        Assert.Equal(RequestResult.Accepted, host.Start(plan.ToBytes()));
        var runner = new NetworkMatchRunner(host, cluster.RunnerOptions(cluster.HostPeer));
        cluster.AdoptHost(runner, FixtureHostKey);
        cluster.RunTicks(300);
        File.WriteAllBytes(Path.Combine(Folder, file), cluster.Store.Files[runner.Save("1")!.Stem]);
    }
}
