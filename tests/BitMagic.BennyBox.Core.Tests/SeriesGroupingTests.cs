using BitMagic.BennyBox.Core.Models;
using BitMagic.BennyBox.Core.Services;

namespace BitMagic.BennyBox.Core.Tests;

public class SeriesGroupingTests
{
    private static Series Make(string name, string? date = null, string? plot = null) => new()
    {
        ProfileId = Guid.Empty,
        SourceSeriesId = Guid.NewGuid().ToString(),
        Name = name,
        ReleaseDate = date,
        Plot = plot,
    };

    [Fact]
    public void SameNameAndDate_AreMerged()
    {
        var groups = SeriesGrouping.Merge([
            Make("The Sopranos", "1999-01-09"),
            Make("The Wire", "2002-06-02"),
            Make("The Sopranos", "1999-01-09"),
            Make("The Sopranos", "1999-01-09"),
        ]);

        Assert.Equal(2, groups.Count);
        Assert.Equal("The Sopranos", groups[0].Label);
        Assert.Equal(3, groups[0].Parts.Count);
        Assert.Equal("The Wire", groups[1].Label);
    }

    [Fact]
    public void SameNameAndPlot_AreMerged_EvenWithDifferentDates()
    {
        var groups = SeriesGrouping.Merge([
            Make("The 1% Club", "2025-06-10", "A game show."),
            Make("The 1% Club", "2024-06-03", "A game show."),
        ]);

        Assert.Single(groups);
        Assert.Equal("The 1% Club", groups[0].Label);
    }

    [Fact]
    public void DifferentShowsWithSameName_StaySeparate_WithYear()
    {
        var groups = SeriesGrouping.Merge([
            Make("The Twilight Zone", "2019-04-01", "Reboot."),
            Make("The Twilight Zone", "1959-10-02", "Original."),
        ]);

        Assert.Equal(2, groups.Count);
        Assert.Equal("The Twilight Zone (2019)", groups[0].Label);
        Assert.Equal("The Twilight Zone (1959)", groups[1].Label);
    }

    [Fact]
    public void NoDateOrPlot_IsNeverMerged()
    {
        var groups = SeriesGrouping.Merge([Make("First Dates"), Make("First Dates")]);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal("First Dates", g.Label));
    }
}
