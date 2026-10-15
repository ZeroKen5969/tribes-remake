using System;
using System.Linq;
using Tribes.Net;
using Tribes.Net.Protocol;
using Tribes.Net.Tests.Support;
using Tribes.Net.Transport;
using Tribes.Net.Transport.Loopback;
using Xunit;

namespace Tribes.Net.Tests;

/// <summary>Identity, size limits and failure handling that protect peers from each other.</summary>
public sealed class HardeningTests
{
    private static readonly MatchStart Start = new()
    {
        MatchId = Guid.NewGuid(),
        Mode = RoomMode.Endless,
        TickMilliseconds = 120,
        HashIntervalTicks = 8,
        Players = [],
    };

    [Fact]
    public void Room_NeverShowsPlayerKeys_SoNobodyCanTakeOverASlot()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer victim = cluster.AddClient("victim");
        cluster.RunUntilJoined(victim);

        RoomState seen = victim.Session.Room!;
        Assert.DoesNotContain(seen.Players, p => p.Id == victim.Id || p.Id == cluster.HostOptions.PlayerId);
        Assert.Equal(victim.PublicId, seen.Players.Single(p => p.Name == "victim").Id);

        // An attacker who copies the visible id gets a slot of its own, not the victim's.
        using var attacker = new RawLoopbackClient(cluster, "attacker");
        attacker.Hello(victim.PublicId);
        cluster.Run(TimeSpan.FromMilliseconds(100));

        Assert.Equal(ClientPhase.Lobby, victim.Session.Phase);
        Assert.Equal(3, cluster.Host.Room.OccupiedSlots);
        Assert.Equal("victim", cluster.Host.Room.FindPlayer(victim.PublicId)!.Name);
    }

    [Fact]
    public void EmptyPlayerKey_IsRejected()
    {
        using var cluster = new LoopbackCluster();
        using var raw = new RawLoopbackClient(cluster);
        raw.Hello(Guid.Empty);
        cluster.RunUntil(() => raw.Disconnected, "the rejection");

        Assert.Equal(RejectReason.InvalidRequest, raw.Messages.OfType<RejectMessage>().Single().Reason);
    }

    [Fact]
    public void SystemCommands_AreSpreadOverBatches_SoNoBatchIsTooLarge()
    {
        var match = new HostMatch(Start, new HashChainSimulation(Start), new LockstepOptions(), new MatchLedger([]));
        for (int i = 0; i < 100; i++)
        {
            match.QueueSystem(PlayerCommand.System(i % 4, i % 2 == 0 ? CommandKind.PlayerAbsent : CommandKind.PlayerReturned));
        }

        TickBatch first = match.TakeBatch(0);
        TickBatch second = match.TakeBatch(1);

        Assert.Equal(ProtocolLimits.MaxSystemCommandsPerBatch, first.Commands.Count);
        Assert.Equal(100 - ProtocolLimits.MaxSystemCommandsPerBatch, second.Commands.Count);
        Assert.Equal(CommandKind.PlayerAbsent, second.Commands[0].Kind);
        Assert.Empty(match.PendingSystemCommands);
    }

    [Fact]
    public void BatchPayload_IsCapped_AndEveryBatchFitsInOneMessage()
    {
        var options = new LockstepOptions { MaxCommandBytes = ProtocolLimits.MaxCommandBytes };
        var match = new HostMatch(Start, new HashChainSimulation(Start), options, new MatchLedger([]));
        var payload = new byte[ProtocolLimits.MaxCommandBytes];
        int accepted = 0;
        for (int i = 0; i < 12; i++)
        {
            if (match.Schedule(i % 4, 0, payload, i + 1) == RequestResult.Accepted)
            {
                accepted++;
            }
        }

        // What does not fit into the first batch goes into the next one.
        Assert.Equal(12, accepted);
        for (int i = 0; i < ProtocolLimits.MaxSystemCommandsPerBatch; i++)
        {
            match.QueueSystem(PlayerCommand.Joined(new PlayerInfo { Id = Guid.NewGuid(), Name = new string('n', 24), Slot = 1, Colour = 1 }));
        }

        TickBatch full = match.TakeBatch(0);
        Assert.Equal(ProtocolLimits.MaxBatchPayloadBytes / ProtocolLimits.MaxCommandBytes, full.Commands.Count(c => !c.IsSystem));
        byte[] encoded = MessageCodec.Encode(new BatchMessage(full));
        Assert.True(encoded.Length <= NetProtocol.MaxMessageBytes, $"A full batch takes {encoded.Length} bytes.");
        Assert.Equal(12 - full.Commands.Count(c => !c.IsSystem), match.TakeBatch(1).Commands.Count(c => !c.IsSystem));
        Assert.NotNull(new LockstepOptions { MaxCommandsPerPlayerPerTick = 2000 }.FindProblem());
        Assert.Null(new LockstepOptions { MaxCommandsPerPlayerPerTick = 1008 }.FindProblem());
    }

    [Fact]
    public void ForgedRoundTrips_AreIgnored_AndCannotOverflowTheInputDelay()
    {
        var rtt = new RttEstimator();
        rtt.AddSample(TimeSpan.FromMilliseconds(80));
        rtt.AddSample(TimeSpan.MaxValue);
        rtt.AddSample(TimeSpan.FromDays(-3));
        Assert.Equal(TimeSpan.FromMilliseconds(80), rtt.Smoothed);

        TimeSpan tick = TimeSpan.FromMilliseconds(120);
        Assert.Equal(12, InputDelayController.Compute(TimeSpan.MaxValue, TimeSpan.MaxValue, tick, 1, 12));
        Assert.Equal(1, InputDelayController.Compute(TimeSpan.Zero, TimeSpan.Zero, tick, 1, 12));
    }

    [Fact]
    public void DamagedLengths_DoNotMakeTheDecoderAllocate()
    {
        byte[] small = SnapshotPacker.Compress([1, 2, 3]);
        Assert.Throws<ProtocolException>(() => SnapshotPacker.Decompress(small, int.MaxValue));

        var header = new SnapshotBeginMessage(Start, 0, 10, 0, false, 3, 100, false, Guid.Empty, TestData.NoLedger);
        var assembler = new SnapshotAssembler(header, 100);
        Assert.Throws<ProtocolException>(() => assembler.Add(new SnapshotChunkMessage(0, ReadOnlyMemory<byte>.Empty)));
    }

    [Fact]
    public void Start_ThatTheGameRejects_LeavesTheRoomInTheLobby()
    {
        bool reject = true;
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        var transport = new LoopbackTransport(cluster.Network, "fussy");
        using var host = new HostSession(
            transport,
            cluster.HostOptions with { PlayerId = Guid.NewGuid() },
            start => reject ? throw new FormatException("bad map") : new HashChainSimulation(start),
            cluster.Clock);

        Assert.Throws<FormatException>(() => host.Start(new byte[] { 1 }));
        Assert.Equal(RoomPhase.Lobby, host.Room.Phase);
        Assert.Null(host.Simulation);

        reject = false;
        Assert.Equal(RequestResult.Accepted, host.Start(new byte[] { 1 }));
        Assert.Equal(RoomPhase.InGame, host.Room.Phase);
    }

    [Fact]
    public void ClientOfAnEarlierRun_GetsASnapshot_FromAResumedHost()
    {
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        TestPeer client = cluster.AddClient("client", configure: o => o with { ReconnectDelay = TimeSpan.FromSeconds(8) });
        cluster.StartMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(2));
        byte[] save = cluster.Host.SaveMatch();
        cluster.RunWithCommands(TimeSpan.FromSeconds(4));

        // The host machine drops off the network; the client keeps its simulation and retries.
        cluster.HostTransport.SimulateConnectionLoss();
        cluster.RunUntil(() => client.Session.Phase == ClientPhase.Reconnecting, "the loss");
        cluster.Host.Close();
        cluster.ReplaceHost(() => HostSession.ResumeFromSave(cluster.HostTransport, cluster.HostOptions, save, cluster.CreateHostSimulation, cluster.Clock));
        cluster.Host.Resume();
        cluster.RunUntil(() => client.Session.Phase == ClientPhase.InGame, "the reconnect", TimeSpan.FromSeconds(20));
        cluster.RunWithCommands(TimeSpan.FromSeconds(3));

        // The resumed run has the old tick numbers but other batches: only a snapshot is safe.
        Assert.True(client.MatchStarts[^1].FromSnapshot);
        Assert.Equal(2, client.Simulations.Count);
        Assert.Empty(client.Desyncs);
        Assert.Empty(cluster.HostDesyncs);
        LoopbackCluster.AssertInSync(20, cluster.HostSimulation, client.Simulation);
    }

    [Fact]
    public void LargestGameSetup_FitsInOneMessage()
    {
        var start = Start with
        {
            GameSetup = new byte[NetProtocol.MaxGameSetupBytes],
            Players = Enumerable.Range(0, RoomLimits.MaxPlayers).Select(i => new PlayerInfo { Id = Guid.NewGuid(), Name = new string('n', 24), Slot = i, Colour = i }).ToArray(),
        };
        byte[] begin = MessageCodec.Encode(new SnapshotBeginMessage(start, long.MaxValue, int.MaxValue, 0, true, int.MaxValue, 100, true, Guid.Empty, new LedgerState(start.Players, [long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue])));

        Assert.True(begin.Length <= NetProtocol.MaxMessageBytes);
        using var cluster = new LoopbackCluster(RoomMode.Endless);
        Assert.Throws<ArgumentException>(() => cluster.Host.Start(new byte[NetProtocol.MaxGameSetupBytes + 1]));
    }
}
