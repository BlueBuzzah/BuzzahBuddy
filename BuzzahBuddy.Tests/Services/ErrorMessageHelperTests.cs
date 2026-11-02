using BuzzahBuddy.Services.Glove;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

/// <summary>Errors say what happened and what to do; raw text only ever goes in Detail.</summary>
public class ErrorMessageHelperTests
{
    [Fact]
    public void Empty_is_generic_without_detail()
    {
        var error = ErrorMessageHelper.GetFriendlyError("");
        Assert.Equal("Something Went Wrong", error.Title);
        Assert.Equal("Try again. If it keeps happening, restart both gloves.", error.Message);
        Assert.Null(error.Detail);
    }

    [Fact]
    public void Firmware_profile_error_maps_and_keeps_raw_text_as_detail()
    {
        var error = ErrorMessageHelper.GetFriendlyError("Invalid profile ID");
        Assert.Equal("That Profile Isn't Available", error.Title);
        Assert.Equal("Pick a profile from the list on the Gloves tab.", error.Message);
        Assert.Equal("Invalid profile ID", error.Detail);
    }

    [Fact]
    public void Unknown_text_never_reaches_title_or_message()
    {
        var error = ErrorMessageHelper.GetFriendlyError("ERR 0x08 weird thing");
        Assert.Equal("Something Went Wrong", error.Title);
        Assert.DoesNotContain("0x08", error.Message);
        Assert.Equal("ERR 0x08 weird thing", error.Detail);
    }

    [Fact]
    public void Refused_start_for_low_battery_says_charge_both_gloves()
    {
        var error = ErrorMessageHelper.GetFriendlyError("Battery too low");
        Assert.Equal("Charge Your Gloves", error.Title);
        Assert.Equal("A glove battery is too low to finish a session. Charge both gloves, then try again.", error.Message);
        Assert.Equal("Battery too low", error.Detail);
    }

    [Fact]
    public void Refused_start_without_secondary_matches_the_session_banner()
    {
        var error = ErrorMessageHelper.GetFriendlyError("Secondary not connected");
        Assert.Equal("Secondary Glove Not Connected", error.Title);
        Assert.Equal(ErrorMessageHelper.SecondaryNotLinkedMessage, error.Message);
        Assert.Equal("Secondary not connected", error.Detail);
    }

    [Theory]
    [InlineData("Primary glove error", "Restart Your Primary Glove", "Restart your primary glove")]
    [InlineData("Secondary glove error", "Restart Your Secondary Glove", "Restart your secondary glove")]
    public void Refused_start_for_glove_error_says_restart_that_glove(string raw, string title, string start)
    {
        var error = ErrorMessageHelper.GetFriendlyError(raw);
        Assert.Equal(title, error.Title);
        Assert.StartsWith(start, error.Message);
        Assert.Contains("motor connections", error.Message);
        Assert.Equal(raw, error.Detail);
    }

    [Fact]
    public void Refused_start_in_wrong_state_says_try_again()
    {
        var error = ErrorMessageHelper.GetFriendlyError("Cannot start in current state");
        Assert.Equal("Can't Start Right Now", error.Title);
        Assert.Equal("Wait a moment, then try again.", error.Message);
        Assert.Equal("Cannot start in current state", error.Detail);
    }

    [Fact]
    public void Test_motors_relay_without_secondary_keeps_its_own_advice()
    {
        var error = ErrorMessageHelper.GetFriendlyError("Secondary glove not connected");
        Assert.NotEqual(ErrorMessageHelper.SecondaryNotLinkedMessage, error.Message);
    }

    [Fact]
    public void Unknown_command_points_to_the_updater()
    {
        var error = ErrorMessageHelper.GetFriendlyError("Unknown command: FOO");
        Assert.Equal("Your Glove Needs an Update", error.Title);
        Assert.Contains("BlueBuzzah Updater", error.Message);
    }

    [Fact]
    public void Timeout_exception()
    {
        var error = ErrorMessageHelper.FromException(new TimeoutException("Command 'INFO' timed out after 5000ms"));
        Assert.Equal("Your Glove Didn't Answer", error.Title);
        Assert.Equal("Move your phone closer to your primary glove, then try again.", error.Message);
        Assert.Equal("Command 'INFO' timed out after 5000ms", error.Detail);
    }

    [Fact]
    public void Other_exceptions_are_generic()
    {
        var error = ErrorMessageHelper.FromException(new ArgumentException("Jitter must be 0-50%", "desired"));
        Assert.Equal("Something Went Wrong", error.Title);
        Assert.DoesNotContain("Parameter", error.Message);
    }

    [Fact]
    public void Alert_text_puts_detail_underneath()
    {
        var error = new UserError("T", "Do this.", "RAW");
        Assert.Equal("Do this.\n\nDetails: RAW", error.ForAlert);
        Assert.Equal("Do this.", new UserError("T", "Do this.", null).ForAlert);
    }
}
