using System;
using System.Buffers.Binary;
using Tribes.Client.Ui.Logic;
using Xunit;

namespace Tribes.Client.Ui.Tests;

/// <summary>
/// The load screen shows each save's picture (a 160x90 PNG next to it) when the file is a PNG of
/// a sensible size, else a stand-in; the picture is shown full size when the row has room for it
/// beside the text and buttons, at half size otherwise.
/// </summary>
public sealed class SaveThumbnailsTests
{
    /// <summary>The bytes a PNG file ends with: an empty IEND chunk and its checksum.</summary>
    private static readonly byte[] End = { 0, 0, 0, 0, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

    private static byte[] Png(int width, int height, int length = 64)
    {
        byte[] data = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), 13);
        "IHDR"u8.CopyTo(data.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), height);
        End.CopyTo(data, length - End.Length);
        return data;
    }

    [Fact]
    public void A_png_of_the_saved_size_is_used()
    {
        Assert.True(SaveThumbnails.IsUsable(Png(160, 90)));
        Assert.True(SaveThumbnails.TryReadSize(Png(160, 90), out int width, out int height));
        Assert.Equal((160, 90), (width, height));
    }

    [Fact]
    public void Other_sensible_sizes_are_used_too()
    {
        Assert.True(SaveThumbnails.IsUsable(Png(320, 180)));
        Assert.True(SaveThumbnails.IsUsable(Png(1, 1)));
    }

    [Fact]
    public void An_empty_file_is_not_used()
    {
        Assert.False(SaveThumbnails.IsUsable([]));
        Assert.False(SaveThumbnails.TryReadSize([], out _, out _));
    }

    [Fact]
    public void A_cut_off_file_is_not_used()
    {
        Assert.False(SaveThumbnails.IsUsable(Png(160, 90).AsSpan(0, 20)));
        // The header is whole but the file stops before its end chunk.
        Assert.False(SaveThumbnails.IsUsable(Png(160, 90, 200).AsSpan(0, 120)));
    }

    [Fact]
    public void A_file_that_is_not_a_png_is_not_used()
    {
        byte[] jpeg = Png(160, 90);
        jpeg[0] = 0xFF;
        jpeg[1] = 0xD8;
        Assert.False(SaveThumbnails.IsUsable(jpeg));

        byte[] noHeader = Png(160, 90);
        "IDAT"u8.CopyTo(noHeader.AsSpan(12));
        Assert.False(SaveThumbnails.IsUsable(noHeader));
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(160, 0)]
    [InlineData(-5, 90)]
    [InlineData(5000, 90)]
    [InlineData(160, 5000)]
    public void A_picture_of_no_or_a_huge_size_is_not_used(int width, int height)
    {
        Assert.False(SaveThumbnails.IsUsable(Png(width, height)));
    }

    [Fact]
    public void A_file_too_long_for_a_thumbnail_is_not_read()
    {
        Assert.True(SaveThumbnails.IsSmallEnough(40_000));
        Assert.False(SaveThumbnails.IsSmallEnough(SaveThumbnails.MaxFileBytes + 1));
        Assert.False(SaveThumbnails.IsSmallEnough(-1));
        Assert.False(SaveThumbnails.IsSmallEnough(0));
        Assert.True(SaveThumbnails.IsSmallEnough(SaveThumbnails.MaxFileBytes));
    }

    [Fact]
    public void Wide_rows_show_the_picture_full_size()
    {
        Assert.Equal((160, 90), SaveThumbnails.DisplaySize(600));
        Assert.Equal((160, 90), SaveThumbnails.DisplaySize(SaveThumbnails.FullSizeRowWidth));
    }

    [Fact]
    public void Narrow_rows_show_it_at_half_size()
    {
        Assert.Equal((80, 45), SaveThumbnails.DisplaySize(SaveThumbnails.FullSizeRowWidth - 1));
        Assert.Equal((80, 45), SaveThumbnails.DisplaySize(300));
        Assert.Equal((80, 45), SaveThumbnails.DisplaySize(0));
    }
}
