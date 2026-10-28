using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Glove;

/// <summary>
/// An error as people read it (design system README "Voice"): the title says what happened,
/// the message says what to do next, and the raw firmware or exception text goes only in
/// <see cref="Detail"/>, shown underneath in code-s, never in the headline.
/// </summary>
public sealed record UserError(string Title, string Message, string? Detail)
{
    /// <summary>For a native alert, which can't style text: the detail goes underneath.</summary>
    public string ForAlert => Detail is null ? Message : $"{Message}\n\nDetails: {Detail}";
}

/// <summary>
/// Turns BlueBuzzah firmware error strings and exceptions into <see cref="UserError"/>s.
/// </summary>
public static class ErrorMessageHelper
{
    private const string Generic = "Something Went Wrong";
    private const string TryAgain = "Try again. If it keeps happening, restart both gloves.";

    /// <summary>Maps a firmware error string; the original text is kept as the detail.</summary>
    public static UserError GetFriendlyError(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            return new(Generic, TryAgain, null);

        var detail = errorMessage;
        var lowerError = errorMessage.ToLowerInvariant();

        if (lowerError.Contains("secondary"))
            return new("Secondary Glove Not Connected",
                "Turn on your secondary glove and make sure it's charged. Then restart both gloves and try again.", detail);

        if (lowerError.Contains("sync not ready"))
            return new("Gloves Are Still Pairing Up",
                "Wait a few seconds, then try again. If it keeps happening, restart both gloves.", detail);

        if (lowerError.Contains("battery too low") || lowerError.Contains("low battery") || lowerError.Contains("charge"))
            return new("Charge Your Gloves",
                "The battery is too low to start a session. Charge both gloves, then try again.", detail);

        if (lowerError.Contains("session active") || lowerError.Contains("session running") || lowerError.Contains("already running"))
            return new("A Session Is Already Running", "Stop the current session before starting a new one.", detail);

        if (lowerError.Contains("cannot modify") || lowerError.Contains("session must be stopped") || lowerError.Contains("must be idle"))
            return new("Stop Your Session First", "Settings can't change while a session runs.", detail);

        if (lowerError.Contains("use:") && (lowerError.Contains("true/false") || lowerError.Contains("1/0")))
            return new("That Setting Didn't Work", TryAgain, detail);

        if (lowerError.Contains("invalid") && (lowerError.Contains("parameter") || lowerError.Contains("value")))
            return new("That Value Is Out of Range", "Check the range under each field, then try again.", detail);

        if (lowerError.Contains("invalid profile") || lowerError.Contains("profile not found"))
            return new("That Profile Isn't Available", "Pick a profile from the list on the Gloves tab.", detail);

        if (lowerError.Contains("profile manager not available"))
            return new("Your Glove Is Still Starting",
                "Wait a moment and try again. If it keeps happening, restart both gloves.", detail);

        if (lowerError.Contains("not in calibration") || lowerError.Contains("calibration mode"))
            return new("Start Test Mode First", "Tap Start Test Mode, then try again.", detail);

        if (lowerError.Contains("invalid finger") || lowerError.Contains("finger index"))
            return new("That Finger Isn't Available", "Pick one of the finger buttons on this screen.", detail);

        if (lowerError.Contains("timeout") || lowerError.Contains("no response"))
            return new("Your Glove Didn't Answer", "Move your phone closer to your primary glove, then try again.", detail);

        if (lowerError.Contains("disconnected") || lowerError.Contains("not connected"))
            return new("Connection Lost", "Connect again on the Gloves tab, then try again.", detail);

        // More specific than the "invalid command" check below.
        if (lowerError.Contains("invalid command format"))
            return new("Your Glove Didn't Understand", TryAgain, detail);

        if (lowerError.Contains("unknown command") || lowerError.Contains("invalid command"))
            return new("Your Glove Needs an Update",
                "Update the glove firmware with the BlueBuzzah Updater on a computer, then try again.", detail);

        return new(Generic, TryAgain, detail);
    }

    /// <summary>Maps an exception; its message is kept as the detail.</summary>
    public static UserError FromException(Exception ex) => ex switch
    {
        BlueBuzzahCommandException command => GetFriendlyError(command.Message),
        TimeoutException => new("Your Glove Didn't Answer",
            "Move your phone closer to your primary glove, then try again.", ex.Message),
        _ => new(Generic, TryAgain, ex.Message),
    };
}
