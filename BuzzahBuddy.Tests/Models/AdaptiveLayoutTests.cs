using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>The width model: two panes by text-relative width, gutter by window width, a capped column.</summary>
public class AdaptiveLayoutTests
{
    [Fact]
    public void Two_panes_start_at_900()
    {
        Assert.True(AdaptiveLayout.For(900, 700, 1).TwoPane);
        Assert.False(AdaptiveLayout.For(899, 700, 1).TwoPane);
    }

    [Fact]
    public void Large_text_counts_against_width()
    {
        Assert.False(AdaptiveLayout.For(1194, 834, 1.35).TwoPane);
        Assert.True(AdaptiveLayout.For(1376, 1032, 1.35).TwoPane);
        Assert.False(AdaptiveLayout.For(1376, 1032, 3.1).TwoPane);
    }

    [Fact]
    public void Small_text_never_widens() =>
        Assert.False(AdaptiveLayout.For(899, 700, 0.8).TwoPane);

    [Fact]
    public void Narrow_window_on_a_big_screen_is_single_column() =>
        Assert.Equal(new PageLayout(400, 1376, false, 368, 16), AdaptiveLayout.For(400, 1376, 1));

    [Fact]
    public void Gutter_widens_at_600()
    {
        Assert.Equal(16, AdaptiveLayout.For(599, 800, 1).Gutter);
        Assert.Equal(24, AdaptiveLayout.For(600, 800, 1).Gutter);
    }

    [Fact]
    public void Column_caps_at_prose_width_scaled_by_text()
    {
        Assert.Equal(672, AdaptiveLayout.For(1032, 1376, 1).ColumnWidth);
        Assert.Equal(907.2, AdaptiveLayout.For(1032, 1376, 1.35).ColumnWidth, 6);
        Assert.Equal(984, AdaptiveLayout.For(1032, 1376, 2).ColumnWidth);
    }

    [Fact]
    public void Phone_column_fills_the_width() =>
        Assert.Equal(358, AdaptiveLayout.For(390, 844, 1).ColumnWidth);

    [Fact]
    public void Landscape_is_wider_than_tall()
    {
        Assert.True(AdaptiveLayout.For(1376, 1032, 1).Landscape);
        Assert.False(AdaptiveLayout.For(1032, 1376, 1).Landscape);
    }

    [Fact]
    public void Column_is_never_negative_in_a_tiny_window() =>
        Assert.Equal(0, AdaptiveLayout.For(20, 800, 1).ColumnWidth);

    [Fact]
    public void Height_only_change_needs_no_relayout() =>
        Assert.False(AdaptiveLayout.NeedsRelayout(AdaptiveLayout.For(1032, 1376, 1), AdaptiveLayout.For(1032, 1300, 1)));

    [Fact]
    public void Rotation_needs_relayout() =>
        Assert.True(AdaptiveLayout.NeedsRelayout(AdaptiveLayout.For(1032, 1376, 1), AdaptiveLayout.For(1376, 1032, 1)));

    [Fact]
    public void Height_change_that_flips_landscape_needs_relayout() =>
        Assert.True(AdaptiveLayout.NeedsRelayout(AdaptiveLayout.For(800, 700, 1), AdaptiveLayout.For(800, 900, 1)));
}
