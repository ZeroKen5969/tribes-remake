using System;
using System.Buffers.Binary;
using System.IO;

using Tribes.Core.Simulation;
using Tribes.Net;
using Tribes.Net.Transport.Loopback;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

/// <summary>
/// Saves keep working after updates: every build reads every save format from 1 to its own and
/// refuses only newer ones; a host resumes its match on a newer build (the players then join
/// with that build).
/// </summary>
public sealed class SaveCompatibilityTests
{
    private static readonly MatchPlan Plan = new() { Mission = 8, Difficulty = 0, Seed = 71 };

    [Fact]
    public void HostedSave_ResumesOnANewerBuild_AndPlayersOfThatBuildJoin()
    {
        byte[] save;
        Guid hostKey;
        using (var old = new SessionCluster(MultiTribeControl.Instance, seed: 71))
        {
            old.AddClient("Guest");
            old.StartMatch(Plan);
            old.RunTicks(40);
            save = old.Store.Files[old.HostPeer.Runner!.Save("1")!.Stem];
            hostKey = old.HostPeer.Key;
        }
        long savedTick = MatchSaveFile.ReadHeader(save).Tick;

        using var updated = new SessionCluster(MultiTribeControl.Instance, seed: 72);
        updated.Host.Close();
        updated.Host.Dispose();
        HostOptions newer = updated.HostOptions(updated.HostPeer) with { PlayerId = hostKey, GameVersion = "2.0.0", ContentVersion = "content-new" };
        NetworkMatchRunner runner = NetworkMatchRunner.Resume(new LoopbackTransport(updated.Network, SessionCluster.HostAddress), newer, save,
            updated.Factory, updated.RunnerOptions(updated.HostPeer) with { GameVersion = "2.0.0", ContentVersion = "content-new" }, updated.Clock);
        updated.AdoptHost(runner, hostKey);
        Assert.Equal(savedTick, runner.Simulation.Tick);
        Assert.Equal(MatchPhase.WaitingForPlayers, runner.Phase);

        // A player still on the old build cannot join; one on the host's build can.
        var stale = new ClientSession(new LoopbackTransport(updated.Network, "stale"), new ClientOptions
        {
            GameVersion = TestContent.GameVersion, ContentVersion = TestContent.ContentVersion, PlayerId = Guid.NewGuid(), PlayerName = "Old",
        }, updated.Factory, updated.Clock);
        stale.Connect(SessionCluster.HostAddress);
        updated.RunUntil(() =>
        {
            stale.Update();
            return stale.Phase == ClientPhase.Ended;
        }, "the old build to be turned away");
        Assert.Equal(RejectReason.GameVersionMismatch, stale.Rejection);
        stale.Dispose();

        Assert.Equal(RequestResult.Accepted, runner.ContinueResumed());
        updated.RunTicks(60);
        Assert.True(runner.Simulation.Tick > savedTick + 50);
        // The save written now carries the new build's versions.
        SaveHeader written = MatchSaveFile.ReadHeader(updated.Store.Files[runner.Save("2")!.Stem]);
        Assert.Equal(("2.0.0", "content-new"), (written.GameVersion, written.ContentVersion));
    }

    [Fact]
    public void LockstepSnapshot_ReadsFormatsUpToItsOwn_AndRefusesNewer()
    {
        LockstepMatch match = LockstepMatch.Create(TestContent.Content, TestContent.Start(Plan, TestContent.Human(0, "A"), TestContent.Computer(1)));
        match.Step([]);
        byte[] good = match.SaveSnapshot();
        Assert.Equal(LockstepMatch.SnapshotFormat, BinaryPrimitives.ReadUInt16LittleEndian(good.AsSpan(4)));
        match.LoadSnapshot(good);

        byte[] newer = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(newer.AsSpan(4), (ushort)(LockstepMatch.SnapshotFormat + 1));
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => match.LoadSnapshot(newer));
        Assert.Contains("newer", error.Message, StringComparison.Ordinal);
        byte[] zero = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(zero.AsSpan(4), 0);
        Assert.Throws<InvalidDataException>(() => match.LoadSnapshot(zero));
    }

    [Fact]
    public void SavedGame_ReadsFormatsUpToItsOwn_AndRefusesNewer()
    {
        GameSimulation game = GameSimulation.StartMission(TestContent.Content, 8, 0);
        game.Step();
        byte[] good = game.Save();
        int format = BinaryPrimitives.ReadInt32LittleEndian(good.AsSpan(4));
        Assert.True(format >= 1);
        Assert.Equal(game.StateHash(), GameSimulation.Load(TestContent.Content, good).StateHash());

        byte[] newer = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(newer.AsSpan(4), format + 1);
        InvalidDataException error = Assert.Throws<InvalidDataException>(() => GameSimulation.Load(TestContent.Content, newer));
        Assert.Contains("newer", error.Message, StringComparison.Ordinal);
        byte[] zero = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(zero.AsSpan(4), 0);
        Assert.Throws<InvalidDataException>(() => GameSimulation.Load(TestContent.Content, zero));
    }

    [Fact]
    public void SinglePlayerSave_OfAnotherBuild_LoadsWhenItsFormatsAreReadable()
    {
        var store = new MemorySaveStore();
        using (LocalMatchRunner runner = LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup { Match = MatchKind.Custom, Mission = 8 }, TestContent.Runner(store)))
        {
            runner.StepOnce();
            runner.Save("1");
        }
        using LocalMatchRunner loaded = LocalMatchRunner.Load(TestContent.Content, store.Files["custom-1"],
            TestContent.Runner() with { GameVersion = "9.9.9", ContentVersion = "content-other" });
        Assert.Equal(1, loaded.Simulation.Tick);
    }

    [Fact]
    public void NewerSaveFile_IsRefusedWithItsOwnProblem()
    {
        byte[] file = MatchSaveFile.Write(new SaveHeader { Kind = SaveKind.HostedMatch, Match = MatchKind.Versus, GameVersion = "1", ContentVersion = "1" }, [1]);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), MatchSaveFile.FormatVersion + 1);
        var transport = new LoopbackTransport(new LoopbackNetwork(new ManualClock(), 1, false), "h");
        var options = new HostOptions { GameVersion = "1", ContentVersion = "1", PlayerId = Guid.NewGuid(), PlayerName = "H" };
        SaveFileException error = Assert.Throws<SaveFileException>(() =>
            NetworkMatchRunner.Resume(transport, options, file, LockstepMatch.Factory(TestContent.Content), TestContent.Runner()));
        Assert.Equal(SaveProblem.NewerFormat, error.Problem);
    }

    [Fact]
    public void NetSave_VersionsAreRestampedOnlyInTheirFields()
    {
        byte[] save = [.. "TRBS"u8.ToArray(), 1, 0, 3, (byte)'1', (byte)'.', (byte)'0', 2, (byte)'c', (byte)'1', 0xAA, 0xBB];
        byte[] restamped = NetworkMatchRunner.RestampVersions(save, "2.0.0", "c22");
        Assert.Equal([.. "TRBS"u8.ToArray(), 1, 0, 5, .. "2.0.0"u8.ToArray(), 3, .. "c22"u8.ToArray(), 0xAA, 0xBB], restamped);
        Assert.Throws<SaveFileException>(() => NetworkMatchRunner.RestampVersions([1, 2, 3], "a", "b"));
        Assert.Throws<SaveFileException>(() => NetworkMatchRunner.RestampVersions([.. "TRBS"u8.ToArray(), 1, 0, 0x7F], "a", "b"));
    }
}
