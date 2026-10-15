using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Tribes.Client.App;
using Xunit;

namespace Tribes.Client.Ui.Tests;

public sealed class ExtractedContentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "tribes-content-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (string, byte[]) File(string name, string text) => (name, Encoding.UTF8.GetBytes(text));

    private static List<(string, byte[])> Version1() =>
    [
        File("missions.json", "{\"missions\":[]}"),
        File("maps/map-7.json", "lake"),
        File("maps/map-12.json", "removed later"),
        File("rules/units.json", "units"),
    ];

    private static List<(string, byte[])> Version2() =>
    [
        File("missions.json", "{\"missions\":[1]}"),
        File("maps/map-7.json", "lake"),
        File("rules/units.json", "units"),
    ];

    private static string[] FilesIn(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
            .Where(f => f != ".complete")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void Prepare_WritesExactlyTheFiles()
    {
        string folder = ExtractedContent.Prepare(root, Version1());

        Assert.Equal(["maps/map-12.json", "maps/map-7.json", "missions.json", "rules/units.json"], FilesIn(folder));
        Assert.Equal("removed later", System.IO.File.ReadAllText(Path.Combine(folder, "maps", "map-12.json")));
    }

    [Fact]
    public void Prepare_AfterAnUpdate_NoLongerHasFilesTheNewContentDropped()
    {
        string old = ExtractedContent.Prepare(root, Version1());

        string folder = ExtractedContent.Prepare(root, Version2());

        Assert.NotEqual(old, folder);
        Assert.DoesNotContain("maps/map-12.json", FilesIn(folder));
        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void Prepare_WithTheSameContent_ReusesTheFolderWithoutWriting()
    {
        string first = ExtractedContent.Prepare(root, Version1());
        string file = Path.Combine(first, "missions.json");
        DateTime written = System.IO.File.GetLastWriteTimeUtc(file);

        string second = ExtractedContent.Prepare(root, Version1());

        Assert.Equal(first, second);
        Assert.Equal(written, System.IO.File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void Prepare_RemovesTheOldSingleFolderLayout()
    {
        Directory.CreateDirectory(Path.Combine(root, "maps"));
        System.IO.File.WriteAllText(Path.Combine(root, "missions.json"), "old");
        System.IO.File.WriteAllText(Path.Combine(root, "maps", "map-12.json"), "old");

        string folder = ExtractedContent.Prepare(root, Version2());

        Assert.Equal([Path.GetFileName(folder)], Directory.GetDirectories(root).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFiles(root));
    }

    [Fact]
    public void Prepare_ReplacesAFolderLeftHalfWritten()
    {
        string half = Path.Combine(root, ExtractedContent.FolderName(Version2()));
        Directory.CreateDirectory(half);
        System.IO.File.WriteAllText(Path.Combine(half, "junk.json"), "half");

        string folder = ExtractedContent.Prepare(root, Version2());

        Assert.Equal(half, folder);
        Assert.Equal(["maps/map-7.json", "missions.json", "rules/units.json"], FilesIn(folder));
    }

    [Fact]
    public void Prepare_WaitsForAFileSomethingElseHoldsForAMoment()
    {
        // A virus scanner or another copy of the game may hold a file there for a moment.
        string half = Path.Combine(root, ExtractedContent.FolderName(Version2()));
        Directory.CreateDirectory(half);
        string held = Path.Combine(half, "junk.json");
        var holder = new FileStream(held, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var release = new System.Threading.Timer(_ => holder.Dispose(), null, 300, System.Threading.Timeout.Infinite);

        string folder = ExtractedContent.Prepare(root, Version2());

        Assert.Equal(["maps/map-7.json", "missions.json", "rules/units.json"], FilesIn(folder));
    }

    [Fact]
    public void Prepare_LeavesAnotherCopysFolderBeingWritten_ButRemovesAnAbandonedOne()
    {
        string busy = Path.Combine(root, "0123456789abcdef.tmp-busy");
        string abandoned = Path.Combine(root, "fedcba9876543210.tmp-abandoned");
        Directory.CreateDirectory(busy);
        Directory.CreateDirectory(abandoned);
        Directory.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));

        ExtractedContent.Prepare(root, Version2());

        Assert.True(Directory.Exists(busy));
        Assert.False(Directory.Exists(abandoned));
    }

    [Fact]
    public void Prepare_FromSeveralCopiesAtOnce_GivesEveryOneTheSameCompleteFolder()
    {
        string[] folders = Enumerable.Range(0, 8)
            .AsParallel()
            .Select(_ => ExtractedContent.Prepare(root, Version1()))
            .ToArray();

        Assert.Single(folders.Distinct());
        Assert.Equal(["maps/map-12.json", "maps/map-7.json", "missions.json", "rules/units.json"], FilesIn(folders[0]));
    }

    [Fact]
    public void FolderName_DependsOnNamesAndBytes_NotOnOrder()
    {
        List<(string, byte[])> files = Version1();
        string name = ExtractedContent.FolderName(files);

        Assert.Equal(name, ExtractedContent.FolderName(Enumerable.Reverse(files).ToList()));
        Assert.NotEqual(name, ExtractedContent.FolderName([.. files.Take(3), File("rules/units.json", "units!")]));
        Assert.NotEqual(name, ExtractedContent.FolderName([.. files.Take(3), File("rules/unit.json", "units")]));
        Assert.Equal(16, name.Length);
    }

    [Fact]
    public void Prepare_WithoutFiles_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => ExtractedContent.Prepare(root, []));
    }
}
