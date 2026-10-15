using System;
using System.IO;

namespace Tribes.Session;

/// <summary>
/// Saves as files in one folder: <c>&lt;stem&gt;.save</c> (the save), <c>&lt;stem&gt;.json</c> (a
/// description for load menus, when a describer is given) and <c>&lt;stem&gt;.png</c> (its picture).
/// Every file is written to a temporary name and moved over the old one, so a crash while saving
/// never destroys the previous save. Replacing a save removes the old picture first; the new one
/// follows when it is ready, and if it never comes the slot has no picture rather than a wrong one.
/// </summary>
public class DirectorySaveStore : ISaveStore
{
    private readonly Func<SaveSlot, byte[]>? describe;

    /// <summary>A store in <paramref name="folder"/> (a plain file system path).</summary>
    /// <param name="folder">Where the files go; created when missing.</param>
    /// <param name="describe">Makes the description file of a save, or null for none.</param>
    public DirectorySaveStore(string folder, Func<SaveSlot, byte[]>? describe = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        Folder = folder;
        this.describe = describe;
    }

    /// <summary>The folder.</summary>
    public string Folder { get; }

    /// <summary>The path of a file of <paramref name="slot"/> with <paramref name="extension"/> (".save", ".json", ".png").</summary>
    public string PathOf(SaveSlot slot, string extension)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return Path.Combine(Folder, slot.Stem + extension);
    }

    /// <inheritdoc />
    public void Write(SaveSlot slot, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(data);
        Directory.CreateDirectory(Folder);
        // The old picture shows the save being replaced: it goes before the save changes.
        File.Delete(PathOf(slot, ".png"));
        Replace(PathOf(slot, ".save"), data);
        if (describe is not null)
        {
            Replace(PathOf(slot, ".json"), describe(slot));
        }
    }

    /// <inheritdoc />
    public void WritePicture(SaveSlot slot, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(png);
        Directory.CreateDirectory(Folder);
        Replace(PathOf(slot, ".png"), png);
    }

    /// <summary>Writes <paramref name="bytes"/> to a temporary file and moves it over <paramref name="path"/>.</summary>
    protected virtual void Replace(string path, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(bytes);
        string temporary = path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
