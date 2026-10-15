using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Tribes.Session.Tests.Support;
using Xunit;

namespace Tribes.Session.Tests;

/// <summary>A save and its picture are replaced together: a slot never shows the picture of another save.</summary>
public sealed class SavePictureTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "tribes-save-pictures-" + Guid.NewGuid().ToString("N"));

    private static Pixels Picture(byte shade, int width = 320, int height = 180) =>
        new(width, height, 4, Enumerable.Repeat(shade, width * height * 4).ToArray());

    private static LocalMatchRunner Start(ISaveStore store, List<string>? log = null) =>
        LocalMatchRunner.Start(TestContent.Content, new LocalMatchSetup { Match = MatchKind.Custom, Mission = 8, Seed = 3 },
            TestContent.Runner(store, 0, log, background: true));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void DirectoryStore_ReplacesTheSave_AndDropsTheOldPictureFirst()
    {
        var store = new DirectorySaveStore(folder, slot => Encoding.UTF8.GetBytes(slot.Stem));
        var slot = new SaveSlot(new SaveHeader { Kind = SaveKind.SinglePlayer, Match = MatchKind.Custom, GameVersion = "1", ContentVersion = "1" }, "2");
        store.Write(slot, [1, 2, 3]);
        store.WritePicture(slot, [9, 9]);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(store.PathOf(slot, ".save")));
        Assert.Equal("custom-2", File.ReadAllText(store.PathOf(slot, ".json")));
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(store.PathOf(slot, ".png")));

        store.Write(slot, [4, 5]);
        Assert.False(File.Exists(store.PathOf(slot, ".png")));
        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(store.PathOf(slot, ".save")));
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        Assert.Throws<ArgumentException>(() => new DirectorySaveStore(""));
    }

    [Fact]
    public void APicture_FollowsItsSave_AndIsShrunkAndEncoded()
    {
        var store = new MemorySaveStore();
        using LocalMatchRunner runner = Start(store);
        Assert.False(runner.AttachPicture("1", Picture(1)));
        Assert.NotNull(runner.Save("1"));
        Assert.True(runner.AttachPicture("1", Picture(200)));
        Assert.True(runner.FlushSaves());
        byte[] png = store.Pictures["custom-1"];
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        // IHDR: 160 x 90 after one halving.
        Assert.Equal(160, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);
        Assert.Equal(90, (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);
        Assert.False(runner.AttachPicture("7", Picture(1)));
        Assert.Throws<ArgumentException>(() => runner.AttachPicture("1", new Pixels(2, 2, 4, [1])));
    }

    [Fact]
    public void OverwritingASlot_LeavesNoPictureOfTheOldSave_EvenWhenTheNewPictureFails()
    {
        var store = new MemorySaveStore();
        var log = new List<string>();
        using LocalMatchRunner runner = Start(store, log);
        runner.Save("3");
        runner.AttachPicture("3", Picture(10));
        Assert.True(runner.FlushSaves());
        Assert.True(store.Pictures.ContainsKey("custom-3"));

        runner.StepOnce();
        store.FailPictures = true;
        runner.Save("3");
        runner.AttachPicture("3", Picture(20));
        Assert.True(runner.FlushSaves());
        Assert.False(store.Pictures.ContainsKey("custom-3"));
        Assert.Equal(1, MatchSaveFile.ReadHeader(store.Files["custom-3"]).Tick);
        Assert.Contains(log, l => l.Contains("picture of custom-3 not written", StringComparison.Ordinal));
    }

    [Fact]
    public void APictureTakenAfterANewerSaveWasAsked_IsDropped()
    {
        var store = new MemorySaveStore();
        using LocalMatchRunner runner = Start(store);
        runner.Save("auto");
        runner.StepOnce();
        runner.Save("auto");
        // The picture is meant for the newest save of the slot at the time it is attached...
        runner.AttachPicture("auto", Picture(30));
        Assert.True(runner.FlushSaves());
        Assert.True(store.Pictures.ContainsKey("custom-auto"));

        // ...and a save asked after it removes it again: the slot then has no picture.
        runner.StepOnce();
        runner.Save("auto");
        Assert.True(runner.FlushSaves());
        Assert.False(store.Pictures.ContainsKey("custom-auto"));
    }

    [Fact]
    public void AFailedSave_GetsNoPicture()
    {
        var store = new MemorySaveStore { Fail = true };
        using LocalMatchRunner runner = Start(store);
        runner.Save("4");
        runner.AttachPicture("4", Picture(40));
        Assert.False(runner.FlushSaves());
        Assert.Empty(store.Pictures);
    }
}
