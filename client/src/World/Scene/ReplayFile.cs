using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Tribes.Core.Content;
using Tribes.Core.World;

namespace Tribes.Client.World.Scene;

/// <summary>
/// A dev replay (<c>.trbr</c>): consecutive frames of the translated original, each a saved world
/// with its screen sidecar, gzip-compressed. The dev scene plays it at the rules' pace to show
/// animation, smooth motion and the frame rate.
/// </summary>
public static class ReplayFile
{
    private const int Magic = 0x52425254; // "TRBR"
    private const int FormatVersion = 1;

    /// <summary>One frame of a replay.</summary>
    /// <param name="Frame">Presented frame number.</param>
    /// <param name="World">The saved world (<see cref="WorldSnapshot"/> bytes).</param>
    /// <param name="View">The sidecar.</param>
    public sealed record Entry(int Frame, byte[] World, SnapshotView View)
    {
        /// <summary>The world of the frame.</summary>
        public GameWorld Load(GameContent content)
        {
            using var stream = new MemoryStream(World, false);
            return WorldSnapshot.Load(stream, content);
        }
    }

    /// <summary>Writes <paramref name="entries"/> to <paramref name="output"/>.</summary>
    public static void Write(Stream output, IReadOnlyList<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(entries);
        using var zip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        using var writer = new BinaryWriter(zip, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(entries.Count);
        foreach (Entry e in entries)
        {
            writer.Write(e.Frame);
            writer.Write(e.World.Length);
            writer.Write(e.World);
            byte[] json = Encoding.UTF8.GetBytes(e.View.ToJson());
            writer.Write(json.Length);
            writer.Write(json);
        }
    }

    /// <summary>Reads a replay written by <see cref="Write"/>.</summary>
    public static IReadOnlyList<Entry> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var zip = new GZipStream(new MemoryStream(data, false), CompressionMode.Decompress);
        using var reader = new BinaryReader(zip, Encoding.UTF8);
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != FormatVersion)
        {
            throw new InvalidDataException("not a world replay");
        }
        int count = reader.ReadInt32();
        var entries = new List<Entry>(count);
        for (int i = 0; i < count; i++)
        {
            int frame = reader.ReadInt32();
            byte[] world = reader.ReadBytes(reader.ReadInt32());
            string json = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
            entries.Add(new Entry(frame, world, SnapshotView.Parse(json)));
        }
        return entries;
    }
}
