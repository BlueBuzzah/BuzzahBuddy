using BuzzahBuddy.Models;
using Xunit;

namespace BuzzahBuddy.Tests.Models;

/// <summary>
/// Covers the Custom editor's parsing, unit conversion, and deviation logic —
/// previously trapped in the view model, which the test host cannot reference.
/// </summary>
public class CustomProfileFormTests
{
    private static CustomProfileForm ValidForm() => new()
    {
        TimeOnMsText = "100",
        TimeOffMsText = "67",
        SessionMinutesText = "120",
        AmplitudeMinText = "70",
        AmplitudeMaxText = "100",
        JitterText = "23.5",
        Mirror = false,
        Fingers = 4,
    };

    [Fact]
    public void TryBuildProfile_ConvertsEnteredMillisecondsToModelSeconds()
    {
        Assert.True(ValidForm().TryBuildProfile(out var profile));

        // The entries are milliseconds; TherapyProfile carries seconds. Getting
        // this backwards would change therapy timing by 1000x.
        Assert.Equal(0.100, profile.TimeOn, precision: 6);
        Assert.Equal(0.067, profile.TimeOff, precision: 6);
    }

    [Fact]
    public void TryBuildProfile_RoundTripsEveryEditableField()
    {
        var form = ValidForm();
        form.SessionMinutesText = "30";
        form.AmplitudeMinText = "40";
        form.AmplitudeMaxText = "90";
        form.JitterText = "12.5";
        form.Mirror = true;

        Assert.True(form.TryBuildProfile(out var profile));

        Assert.Equal(30, profile.TimeSession);
        Assert.Equal(40, profile.AmplitudeMin);
        Assert.Equal(90, profile.AmplitudeMax);
        Assert.Equal(12.5, profile.Jitter);
        Assert.True(profile.Mirror);
    }

    [Fact]
    public void TryBuildProfile_LandsInTheCustomSlot()
    {
        Assert.True(ValidForm().TryBuildProfile(out var profile));

        Assert.True(profile.IsCustom);
    }

    [Fact]
    public void TryBuildProfile_CarriesTheDeviceFingerCountNotAFixedDefault()
    {
        var form = ValidForm();
        form.Fingers = 5;

        Assert.True(form.TryBuildProfile(out var profile));

        Assert.Equal(5, profile.Fingers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("-")]
    public void TryBuildProfile_RejectsUnparseableInput(string badValue)
    {
        var form = ValidForm();
        form.TimeOnMsText = badValue;

        Assert.False(form.TryBuildProfile(out _));
    }

    [Fact]
    public void PopulateFrom_LeavesTheFingerCountAlone()
    {
        // Only a device read may change Fingers. If "Use Research Defaults" could
        // move it, a five-motor glove would silently drop to four active fingers
        // with no field on screen to show it.
        var form = ValidForm();
        form.Fingers = 3;

        // Deliberately mismatched: For(5) carries Fingers = 5, so if PopulateFrom
        // copied it this would become 5 rather than staying 3.
        form.PopulateFrom(ResearchDefaults.For(5));

        Assert.Equal(3, form.Fingers);
    }

    [Fact]
    public void PopulateFrom_ThenTryBuildProfile_RoundTripsCleanly()
    {
        var original = ResearchDefaults.For(4);
        var form = new CustomProfileForm { Fingers = original.Fingers };
        form.PopulateFrom(original);

        Assert.True(form.TryBuildProfile(out var rebuilt));

        Assert.Empty(ResearchDefaults.DeviatingFields(rebuilt, 4));
    }

    [Fact]
    public void DeviatingFields_AtResearchDefaults_IsEmpty()
        => Assert.Empty(ValidForm().DeviatingFields(4));

    [Fact]
    public void DeviatingFields_NamesTheChangedField()
    {
        var form = ValidForm();
        form.JitterText = "0";

        Assert.Equal(new[] { nameof(TherapyProfile.Jitter) }, form.DeviatingFields(4));
    }

    [Fact]
    public void DeviatingFields_WhileHalfTyped_ReportsNothingRatherThanGuessing()
    {
        var form = ValidForm();
        form.AmplitudeMinText = "";   // user cleared the field to retype it

        Assert.Empty(form.DeviatingFields(4));
    }

    [Fact]
    public void DerivedTimingText_ReportsTheCoordinatedResetPeriod()
        => Assert.Equal("CR period 668 ms · 1.50 Hz", ValidForm().DerivedTimingText);

    [Fact]
    public void DerivedTimingText_UsesTheDeviceFingerCount()
    {
        var form = ValidForm();
        form.Fingers = 5;

        // A five-motor glove lengthens the CR period at identical burst timing.
        Assert.Equal("CR period 835 ms · 1.20 Hz", form.DerivedTimingText);
    }

    [Fact]
    public void DerivedTimingText_WhileHalfTyped_SaysUnavailable()
    {
        var form = ValidForm();
        form.TimeOffMsText = "";

        Assert.Equal("CR period unavailable", form.DerivedTimingText);
    }

    [Fact]
    public void EffectiveJitterCapText_WithinTheCap_IsNull()
    {
        // Null, not empty string — the page binds IsVisible through IsNotNullConverter.
        Assert.Null(ValidForm().EffectiveJitterCapText);
    }

    [Fact]
    public void EffectiveJitterCapText_AboveTheCap_WarnsWithTheActualCap()
    {
        var form = ValidForm();
        form.TimeOnMsText = "200";
        form.TimeOffMsText = "30";
        form.JitterText = "50";

        var text = form.EffectiveJitterCapText;

        Assert.NotNull(text);
        Assert.Contains("21.7%", text);   // 200 * (30 - 5) / 230
    }

    [Fact]
    public void EffectiveJitterCapText_WhileHalfTyped_IsNull()
    {
        var form = ValidForm();
        form.JitterText = "";

        Assert.Null(form.EffectiveJitterCapText);
    }
}
