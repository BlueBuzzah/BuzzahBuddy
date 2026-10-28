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
        Assert.Equal("Something went wrong", error.Title);
        Assert.Equal("Try again. If it keeps happening, restart both gloves.", error.Message);
        Assert.Null(error.Detail);
    }

    [Fact]
    public void Firmware_profile_error_maps_and_keeps_raw_text_as_detail()
    {
        var error = ErrorMessageHelper.GetFriendlyError("Invalid profile ID");
        Assert.Equal("That profile isn't available", error.Title);
        Assert.Equal("Pick a profile from the list on the Gloves tab.", error.Message);
        Assert.Equal("Invalid profile ID", error.Detail);
    }

    [Fact]
    public void Unknown_text_never_reaches_title_or_message()
    {
        var error = ErrorMessageHelper.GetFriendlyError("ERR 0x08 weird thing");
        Assert.Equal("Something went wrong", error.Title);
        Assert.DoesNotContain("0x08", error.Message);
        Assert.Equal("ERR 0x08 weird thing", error.Detail);
    }

    [Fact]
    public void Unknown_command_points_to_the_updater()
    {
        var error = ErrorMessageHelper.GetFriendlyError("Unknown command: FOO");
        Assert.Equal("Your glove needs an update", error.Title);
        Assert.Contains("BlueBuzzah Updater", error.Message);
    }

    [Fact]
    public void Timeout_exception()
    {
        var error = ErrorMessageHelper.FromException(new TimeoutException("Command 'INFO' timed out after 5000ms"));
        Assert.Equal("Your glove didn't answer", error.Title);
        Assert.Equal("Move your phone closer to your primary glove, then try again.", error.Message);
        Assert.Equal("Command 'INFO' timed out after 5000ms", error.Detail);
    }

    [Fact]
    public void Other_exceptions_are_generic()
    {
        var error = ErrorMessageHelper.FromException(new ArgumentException("Jitter must be 0-50%", "desired"));
        Assert.Equal("Something went wrong", error.Title);
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
