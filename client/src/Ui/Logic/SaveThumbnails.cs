using System;
using System.Buffers.Binary;

namespace Tribes.Client.Ui.Logic;

/// <summary>
/// The pictures the match screen keeps next to each save (a 160x90 PNG): which files can be
/// shown, and how big the load screen shows them. A file that is missing, cut off, not a PNG or
/// of no or an absurd size gets a stand-in picture instead.
/// </summary>
public static class SaveThumbnails
{
    /// <summary>The size the match screen saves them at.</summary>
    public const int Width = 160, Height = 90;

    /// <summary>The longest side a picture may have to be shown.</summary>
    public const int MaxSide = 1024;

    /// <summary>The longest file read as a picture (a 160x90 PNG is far smaller).</summary>
    public const long MaxFileBytes = 1 << 20;

    /// <summary>The narrowest save row (logical pixels) that shows the picture full size beside its text and buttons.</summary>
    public const float FullSizeRowWidth = 440;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The last chunk of every PNG file: empty "IEND" and its checksum.</summary>
    private static ReadOnlySpan<byte> EndChunk => [0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];

    /// <summary>Reads the picture size from the header of a PNG file; false when it is not one.</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> file, out int width, out int height)
    {
        width = 0;
        height = 0;
        // Signature, then the first chunk must be the 13-byte header: length, "IHDR", width, height.
        if (file.Length < 24 || !file[..8].SequenceEqual(Signature)
            || BinaryPrimitives.ReadInt32BigEndian(file[8..]) != 13 || !file.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return false;
        }
        width = BinaryPrimitives.ReadInt32BigEndian(file[16..]);
        height = BinaryPrimitives.ReadInt32BigEndian(file[20..]);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="file"/> looks like a whole PNG picture that can be shown: its header
    /// gives a sensible size and it ends with the end chunk (a file cut off while written does not).
    /// </summary>
    public static bool IsUsable(ReadOnlySpan<byte> file) =>
        TryReadSize(file, out int width, out int height) && width is > 0 and <= MaxSide && height is > 0 and <= MaxSide
        && file.Length >= 24 + EndChunk.Length && file[^EndChunk.Length..].SequenceEqual(EndChunk);

    /// <summary>Whether a file of <paramref name="length"/> bytes is worth reading as a picture.</summary>
    public static bool IsSmallEnough(long length) => length is > 0 and <= MaxFileBytes;

    /// <summary>The size to show a picture at in a save row <paramref name="rowWidth"/> wide.</summary>
    public static (int Width, int Height) DisplaySize(float rowWidth) =>
        rowWidth >= FullSizeRowWidth ? (Width, Height) : (Width / 2, Height / 2);
}
