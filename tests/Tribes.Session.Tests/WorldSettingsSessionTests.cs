using System;
using System.IO;
using System.Text;
using Tribes.Core.World;
using Tribes.Net;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

/// <summary>The lobby's world settings through the session: the game setup, new worlds, saves.</summary>
public sealed class WorldSettingsSessionTests
{
    private static readonly WorldSettings Chosen = new() { OpeningGraceFrames = 1000, StartStockPercent = 200, WildAnimals = false };

    [Fact]
    public void MatchPlan_CarriesTheSettings_AndRefusesOnesOutOfRange()
    {
        var plan = new MatchPlan { Mission = 8, Seed = 3, Settings = Chosen };

        MatchPlan read = MatchPlan.Read(plan.ToBytes());

        Assert.Equal(Chosen, read.Settings);
        Assert.Null(MatchPlan.Read(new MatchPlan { Mission = 8 }.ToBytes()).Settings);
        Assert.Throws<InvalidDataException>(() => MatchPlan.Read(Encoding.UTF8.GetBytes(
            "{\"mission\": 8, \"difficulty\": 0, \"seed\": 1, \"settings\": {\"openingGraceFrames\": 10, \"startStockPercent\": -5, \"wildAnimals\": true}}")));
        Assert.Equal(5 * MatchPlan.FramesPerMinute, MatchPlan.DefaultMatchSettings.OpeningGraceFrames);
    }

    [Fact]
    public void AMatch_PlaysByThePlansSettings_OrTheDefaultGrace()
    {
        LockstepMatch chosen = LockstepMatch.Create(TestContent.Content,
            TestContent.Start(new MatchPlan { Mission = 8, Seed = 3, Settings = Chosen }, TestContent.Human(0, "Host"), TestContent.Human(1, "Guest")));
        LockstepMatch plain = LockstepMatch.Create(TestContent.Content,
            TestContent.Start(new MatchPlan { Mission = 8, Seed = 3 }, TestContent.Human(0, "Host"), TestContent.Human(1, "Guest")));

        Assert.Equal(Chosen, chosen.Simulation.World.Settings);
        Assert.Equal(MatchPlan.DefaultMatchSettings, plain.Simulation.World.Settings);
    }

    [Fact]
    public void AnEndlessWorld_PlaysByThePlansSettings_OrTheEndlessDefaults()
    {
        var endless = new EndlessPlan(5, 0, 1, 0);
        LockstepMatch chosen = LockstepMatch.Create(TestContent.Content,
            TestContent.Start(new MatchPlan { Mission = -1, Seed = 5, Endless = endless, Settings = Chosen }, TestContent.Human(0, "Host")));
        LockstepMatch plain = LockstepMatch.Create(TestContent.Content,
            TestContent.Start(new MatchPlan { Mission = -1, Seed = 5, Endless = endless }, TestContent.Human(0, "Host")));

        Assert.Equal(Chosen, chosen.Simulation.World.Settings);
        Assert.Equal(TestContent.Content.Endless!.Pacing.OpeningGraceFrames, plain.Simulation.World.Settings.OpeningGraceFrames);
    }

    [Fact]
    public void AGameAlone_KeepsItsSettings_InTheSaveAndItsHeader()
    {
        var store = new MemorySaveStore();
        using LocalMatchRunner runner = LocalMatchRunner.Start(TestContent.Content,
            new LocalMatchSetup { Match = MatchKind.Endless, Seed = 6, ComputerTribes = 1, NewTribes = 0, Settings = Chosen }, TestContent.Runner(store));
        runner.StepOnce();

        SaveSlot slot = runner.Save("1")!;
        using LocalMatchRunner loaded = LocalMatchRunner.Load(TestContent.Content, store.Files[slot.Stem], TestContent.Runner());

        Assert.Equal(Chosen, runner.Simulation.World.Settings);
        Assert.Equal(Chosen, slot.Header.Settings);
        Assert.Equal(Chosen, MatchSaveFile.ReadHeader(store.Files[slot.Stem]).Settings);
        Assert.Equal(Chosen, loaded.Simulation.World.Settings);
    }

    [Fact]
    public void SaveHeader_HoldsSettingsOrNone()
    {
        var header = new SaveHeader
        {
            Kind = SaveKind.SinglePlayer,
            Match = MatchKind.Campaign,
            GameVersion = "1",
            ContentVersion = "c",
            TickMilliseconds = 120,
            SavedAt = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
        };

        Assert.Null(MatchSaveFile.ReadHeader(MatchSaveFile.Write(header, [1])).Settings);
        Assert.Equal(Chosen, MatchSaveFile.ReadHeader(MatchSaveFile.Write(header with { Settings = Chosen }, [1])).Settings);
    }
}
