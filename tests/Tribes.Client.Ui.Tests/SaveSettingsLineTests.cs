using System;
using Tribes.Client.Ui.Logic;
using Xunit;

namespace Tribes.Client.Ui.Tests;

public sealed class SaveSettingsLineTests
{
    private const int FramesPerMinute = 500;

    [Fact]
    public void The_line_tells_difficulty_grace_stock_and_animals()
    {
        SaveSettingsParts parts = SaveSettingsLine.For(1, 15 * FramesPerMinute, 200, wildAnimals: false, FramesPerMinute);

        Assert.Equal(new SaveSettingsParts("difficulty.hard", 15, "2", false), parts);
    }

    [Fact]
    public void The_original_settings_read_as_easy_no_grace_the_maps_stock_and_animals()
    {
        Assert.Equal(new SaveSettingsParts("difficulty.easy", 0, "1", true),
            SaveSettingsLine.For(0, 0, SaveSettingsLine.OriginalStockPercent, wildAnimals: true, FramesPerMinute));
    }

    [Fact]
    public void Odd_stock_and_part_minutes_are_shown_as_they_are()
    {
        SaveSettingsParts parts = SaveSettingsLine.For(0, (2 * FramesPerMinute) + 10, 150, wildAnimals: true, FramesPerMinute);

        Assert.Equal(2, parts.GraceMinutes);
        Assert.Equal("1.5", parts.Stock);
        Assert.Throws<ArgumentOutOfRangeException>(() => SaveSettingsLine.For(0, 0, 100, true, 0));
    }
}
