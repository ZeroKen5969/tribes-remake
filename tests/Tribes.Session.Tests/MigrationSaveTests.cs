using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Transport.Loopback;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

/// <summary>
/// The records stay with whoever hosts: after a host migration the new host autosaves, and that
/// save resumes the match later on the new host's device.
/// </summary>
public sealed class MigrationSaveTests
{
    private static readonly MatchPlan Plan = new() { Mission = 8, Difficulty = 0, Seed = 31 };

    [Fact]
    public void HostAfterAMigration_KeepsAutosaving_AndItsSaveResumesTheMatch()
    {
        using var cluster = new SessionCluster(new TwoHumansControl(), hostMigration: true, seed: 14, autosaveTicks: 300);
        SessionPeer heir = cluster.AddClient("Heir");
        SessionPeer other = cluster.AddClient("Other");
        cluster.StartMatch(Plan);
        cluster.HostPeer.Runner!.SetSpeed(400);
        cluster.RunTicks(200);
        cluster.Settle();
        Guid firstHost = cluster.HostPeer.Runner.LocalPlayerId;

        cluster.HostPeer.Runner.Quit();
        cluster.RunUntil(() => heir.Runner!.IsHost, "the next player to host");
        long tookOver = heir.Tick;
        Assert.Empty(heir.OwnSaves.Written);
        cluster.RunUntil(() => heir.OwnSaves.Written.Any(s => s.Slot == SaveSlot.Auto) && heir.Tick > tookOver + 50, "the new host to autosave");

        Assert.Contains(heir.Notices, n => n.Kind == MatchNoticeKind.Autosaved);
        Assert.Empty(other.OwnSaves.Written);
        byte[] save = heir.OwnSaves.Files["versus-auto"];
        SaveHeader header = MatchSaveFile.ReadHeader(save);
        Assert.Equal(SaveKind.HostedMatch, header.Kind);
        Assert.True(header.Tick > tookOver);

        // Later, on the new host's device: the save opens as its room, waiting for the others,
        // the first host among them, as an ordinary player.
        NetworkMatchRunner resumed = NetworkMatchRunner.Resume(
            new LoopbackTransport(cluster.Network, "heir-later"), cluster.HostOptions(heir), save, cluster.Factory, cluster.RunnerOptions(heir), cluster.Clock);
        try
        {
            Assert.Equal(MatchPhase.WaitingForPlayers, resumed.Phase);
            Assert.Equal(header.Tick, resumed.Simulation.Tick);
            Assert.Equal(heir.Runner!.LocalPlayerId, resumed.LocalPlayerId);
            Assert.Contains(resumed.ExpectedPlayers, p => p.Player.Id == firstHost);
            Assert.Contains(resumed.ExpectedPlayers, p => p.Player.Id == other.Runner!.LocalPlayerId);
        }
        finally
        {
            resumed.Dispose();
        }
    }
}
