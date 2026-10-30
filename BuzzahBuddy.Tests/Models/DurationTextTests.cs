using System.Globalization;
using System.Text.RegularExpressions;
using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>Time text matching the design system's bundle.js clock() and spoken().</summary>
public class DurationTextTests
{
    [Theory]
    [InlineData(6127, "1:42:07")]
    [InlineData(300, "5:00")]
    [InlineData(59, "0:59")]
    [InlineData(7200, "2:00:00")]
    public void Clock(int seconds, string expected) => Assert.Equal(expected, DurationText.Clock(seconds));

    [Theory]
    [InlineData(6127, "1 hour 42 minutes")]
    [InlineData(3600, "1 hour")]
    [InlineData(7200, "2 hours")]
    [InlineData(60, "1 minute")]
    public void Spoken(int seconds, string expected) => Assert.Equal(expected, DurationText.Spoken(seconds));

    // JS Math.round rounds 0.5 up; C# Math.Round defaults to banker's rounding ("0 minutes").
    [Fact]
    public void Spoken_RoundsHalfUp() => Assert.Equal("1 minute", DurationText.Spoken(30));

    [Theory]
    [InlineData(5400, "1h 30min")]
    [InlineData(300, "5min")]
    [InlineData(7200, "2h")]
    public void Short(int seconds, string expected) => Assert.Equal(expected, DurationText.Short(seconds));

    // Both round once to whole minutes, half up, then split, so 59.98 minutes is a whole hour.
    [Theory]
    [InlineData(7199, "2h", "2 hours")]
    [InlineData(3599, "1h", "1 hour")]
    [InlineData(89, "1min", "1 minute")]
    [InlineData(90, "2min", "2 minutes")]
    public void Short_and_Spoken_round_to_the_nearest_minute(int seconds, string shortText, string spokenText)
    {
        Assert.Equal(shortText, DurationText.Short(seconds));
        Assert.Equal(spokenText, DurationText.Spoken(seconds));
    }

    [Fact]
    public void Short_and_Spoken_always_name_the_same_figure()
    {
        for (var seconds = 0; seconds <= 10 * 3600; seconds++)
            Assert.True(Minutes(DurationText.Short(seconds)) == Minutes(DurationText.Spoken(seconds)),
                $"{seconds} s: \"{DurationText.Short(seconds)}\" vs \"{DurationText.Spoken(seconds)}\"");
    }

    // "1h 5min" or "1 hour 5 minutes" as total minutes.
    private static int Minutes(string text)
    {
        var match = Regex.Match(text, @"^(?:(\d+) ?h(?:ours?)?)? ?(?:(\d+) ?min(?:utes?)?)?$");
        Assert.True(match.Success, text);
        int Part(int i) => match.Groups[i].Success ? int.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        return Part(1) * 60 + Part(2);
    }

    [Fact]
    public void Accessible_running() =>
        Assert.Equal("Session running, 1 hour 42 minutes left", SessionRingText.Accessible(SessionRingState.Running, 6127));

    [Fact]
    public void Accessible_idle() =>
        Assert.Equal("Session ready, 2 hours left", SessionRingText.Accessible(SessionRingState.Idle, 7200));

    [Fact]
    public void Accessible_complete() =>
        Assert.Equal("Session complete", SessionRingText.Accessible(SessionRingState.Complete, 0));

    [Fact]
    public void Accessible_count_up() =>
        Assert.Equal("Session running, 12 minutes so far", SessionRingText.AccessibleCountUp(720));
}
