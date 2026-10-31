using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>GloveStatus reads as one sentence (accessibility.md Screen readers; bundle.js GloveStatus label).</summary>
public class GloveStatusTextTests
{
    // The card shows the role alone; the spoken sentence still says "glove".
    [Fact]
    public void Card_name_is_the_role_alone()
    {
        Assert.Equal("Primary", GloveStatusText.Name(GloveRole.Primary));
        Assert.Equal("Secondary", GloveStatusText.Name(GloveRole.Secondary));
    }

    [Fact]
    public void Connected_with_battery() =>
        Assert.Equal("Primary glove, connected, battery 87 percent",
            GloveStatusText.Describe(new GloveStatusInfo(GloveRole.Primary, GloveLinkStatus.Connected, 87)));

    [Fact]
    public void Low_battery_says_low() =>
        Assert.Equal("Secondary glove, connected, battery 12 percent, low",
            GloveStatusText.Describe(new GloveStatusInfo(GloveRole.Secondary, GloveLinkStatus.Connected, 12)));

    [Fact]
    public void No_reading() =>
        Assert.Equal("Secondary glove, no reading",
            GloveStatusText.Describe(new GloveStatusInfo(GloveRole.Secondary, GloveLinkStatus.NoReading, null)));

    // A stale battery value must not be read out for a glove that isn't connected.
    [Fact]
    public void Disconnected_hides_battery() =>
        Assert.Equal("Primary glove, not connected",
            GloveStatusText.Describe(new GloveStatusInfo(GloveRole.Primary, GloveLinkStatus.Disconnected, 50)));

    [Theory]
    [InlineData(40, false)]
    [InlineData(39, true)]
    [InlineData(null, false)]
    public void Low_is_under_40(int? percent, bool low) => Assert.Equal(low, GloveStatusText.IsLow(percent));

    [Fact]
    public void Badges()
    {
        Assert.Equal((StatusTone.Success, "Connected"), GloveStatusText.Badge(GloveLinkStatus.Connected));
        Assert.Equal((StatusTone.Brand, "Connecting"), GloveStatusText.Badge(GloveLinkStatus.Connecting));
        Assert.Equal((StatusTone.Danger, "Not Connected"), GloveStatusText.Badge(GloveLinkStatus.Disconnected));
        Assert.Equal((StatusTone.Neutral, "No Reading"), GloveStatusText.Badge(GloveLinkStatus.NoReading));
    }

    [Fact]
    public void Sentinel_voltage_formats_as_no_reading() =>
        Assert.Equal("—", BatteryReading.Format(BatteryReading.FromRaw(0.0)));

    [Fact]
    public void Volts_have_two_decimals_and_no_space() =>
        Assert.Equal("3.72V", BatteryReading.FormatVolts(3.72));

    // The Session banner's "Charge your glove", the GloveStatus "Low" and the glove's orange
    // LED use one threshold: the firmware's 3.80 V.
    [Theory]
    [InlineData(3.79, true)]
    [InlineData(3.7999, true)]
    [InlineData(3.80, false)]
    [InlineData(3.81, false)]
    public void Low_voltage_matches_the_low_badge(double volts, bool low)
    {
        Assert.Equal(low, GloveStatusText.IsLowVoltage(volts));
        Assert.Equal(low, GloveStatusText.IsLow(BatteryReading.ToPercentage(volts)));
    }

    [Fact]
    public void No_reading_is_never_low() => Assert.False(GloveStatusText.IsLowVoltage(null));

    [Theory]
    [InlineData(70, LucideIcons.BatteryFull)]
    [InlineData(40, LucideIcons.BatteryMedium)]
    [InlineData(39, LucideIcons.BatteryLow)]
    public void Battery_glyph_turns_low_with_the_word(int percent, string glyph) =>
        Assert.Equal(glyph, GloveStatusText.BatteryGlyph(percent));

    private static GloveStatusInfo Glove(GloveRole role, GloveLinkStatus status, int? percent) => new(role, status, percent);

    [Fact]
    public void Summary_both_connected_lists_batteries() =>
        Assert.Equal((StatusTone.Success, "Gloves Connected", "Primary 87% · Secondary 56%"),
            GloveStatusText.Summary(Glove(GloveRole.Primary, GloveLinkStatus.Connected, 87),
                Glove(GloveRole.Secondary, GloveLinkStatus.Connected, 56)));

    // The word carries the warning too, not just the amber icon.
    [Fact]
    public void Summary_low_battery_warns_and_says_low() =>
        Assert.Equal((StatusTone.Warning, "Gloves Connected", "Primary 87% · Secondary 12% Low"),
            GloveStatusText.Summary(Glove(GloveRole.Primary, GloveLinkStatus.Connected, 87),
                Glove(GloveRole.Secondary, GloveLinkStatus.Connected, 12)));

    // The secondary is only known through the primary's report of its battery.
    [Fact]
    public void Summary_secondary_without_reading_claims_only_the_primary() =>
        Assert.Equal((StatusTone.Warning, "Primary Glove Connected", "Primary 87% · Secondary no reading"),
            GloveStatusText.Summary(Glove(GloveRole.Primary, GloveLinkStatus.Connected, 87),
                Glove(GloveRole.Secondary, GloveLinkStatus.NoReading, null)));

    [Fact]
    public void Summary_before_any_battery_reading_has_no_detail() =>
        Assert.Null(GloveStatusText.Summary(Glove(GloveRole.Primary, GloveLinkStatus.Connected, null),
            Glove(GloveRole.Secondary, GloveLinkStatus.Connected, null)).Detail);

    [Fact]
    public void Summary_while_reconnecting_does_not_claim_connected() =>
        Assert.Equal((StatusTone.Brand, "Gloves Connecting", null),
            GloveStatusText.Summary(Glove(GloveRole.Primary, GloveLinkStatus.Connecting, null),
                Glove(GloveRole.Secondary, GloveLinkStatus.Disconnected, null)));
}
