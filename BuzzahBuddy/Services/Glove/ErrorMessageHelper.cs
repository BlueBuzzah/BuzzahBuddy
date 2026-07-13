using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Glove;

/// <summary>
/// Helper class to convert BlueBuzzah error codes and messages into user-friendly text.
/// Maps technical error messages to actionable, accessible descriptions.
/// </summary>
public static class ErrorMessageHelper
{
    /// <summary>
    /// Converts a BlueBuzzah error message to a user-friendly message with recovery suggestions.
    /// </summary>
    /// <param name="errorMessage">The raw error message from the device</param>
    /// <returns>Tuple of (title, message) for display</returns>
    public static (string Title, string Message) GetFriendlyError(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return ("Error", "An unknown error occurred. Please try again.");
        }

        var lowerError = errorMessage.ToLowerInvariant();

        // Secondary Device Connection Errors
        if (lowerError.Contains("secondary"))
        {
            return ("Second Glove Not Connected",
                "The second glove (Secondary device) is not detected. Please ensure:\n\n" +
                "• Second glove is powered on\n" +
                "• Second glove is charged\n" +
                "• Both gloves are paired\n\n" +
                "Try restarting both gloves.");
        }

        // Battery Errors
        if (lowerError.Contains("battery too low") || lowerError.Contains("low battery") || lowerError.Contains("charge"))
        {
            return ("Battery Too Low",
                "Battery level is too low to start a therapy session.\n\n" +
                "Please charge the gloves before continuing. A session requires at least 3.3V.");
        }

        // Session Active Errors
        if (lowerError.Contains("session active") || lowerError.Contains("session running") || lowerError.Contains("already running"))
        {
            return ("Session Already Running",
                "Cannot start a new session while one is already active.\n\n" +
                "Please stop the current session first.");
        }

        // Profile/Parameter Modification Errors
        if (lowerError.Contains("cannot modify") || lowerError.Contains("session must be stopped") || lowerError.Contains("must be idle"))
        {
            return ("Cannot Modify During Session",
                "Profile and parameter changes are not allowed during an active session.\n\n" +
                "Please stop the current session to change settings.");
        }

        // Invalid Boolean Setting Errors (e.g., "use: true/false" or "use: 1/0")
        if (lowerError.Contains("use:") && (lowerError.Contains("true/false") || lowerError.Contains("1/0")))
        {
            return ("Invalid Setting",
                "An invalid value was sent. Please try again.");
        }

        // Invalid Parameter Errors
        if (lowerError.Contains("invalid") && (lowerError.Contains("parameter") || lowerError.Contains("value")))
        {
            return ("Invalid Parameter",
                "The parameter value is outside the allowed range.\n\n" +
                "Please check the parameter constraints and try again.");
        }

        // Profile Errors
        if (lowerError.Contains("invalid profile") || lowerError.Contains("profile not found"))
        {
            // Per BLE protocol v2.0.0: 6 profiles available
            return ("Invalid Profile",
                "The selected profile is not available.\n\n" +
                "Valid profiles are:\n" +
                "• 1 - Regular VCR\n" +
                "• 2 - Noisy VCR\n" +
                "• 3 - Hybrid VCR\n" +
                "• 4 - Custom VCR\n" +
                "• 5 - Gentle\n" +
                "• 6 - Quick Test");
        }

        // Profile Manager Not Available
        if (lowerError.Contains("profile manager not available"))
        {
            return ("Device Not Ready",
                "The device is still initializing. Please wait a moment and try again, or restart the gloves.");
        }

        // Calibration Errors
        if (lowerError.Contains("not in calibration") || lowerError.Contains("calibration mode"))
        {
            return ("Not in Calibration Mode",
                "This command requires calibration mode to be active.\n\n" +
                "Please enter calibration mode first.");
        }

        if (lowerError.Contains("invalid finger") || lowerError.Contains("finger index"))
        {
            return ("Invalid Finger Selection",
                "That finger isn't available on the connected gloves.\n\n" +
                "Please pick one of the finger buttons shown on the calibration screen.");
        }

        // Connection/Communication Errors
        if (lowerError.Contains("timeout") || lowerError.Contains("no response"))
        {
            return ("Communication Timeout",
                "The glove did not respond in time.\n\n" +
                "Please check the Bluetooth connection and try again.");
        }

        if (lowerError.Contains("disconnected") || lowerError.Contains("not connected"))
        {
            return ("Connection Lost",
                "The Bluetooth connection to the glove was lost.\n\n" +
                "Please reconnect and try again.");
        }

        // Invalid Command Format (more specific, must come before generic "invalid command" check)
        if (lowerError.Contains("invalid command format"))
        {
            return ("Communication Error",
                "The device received a malformed command. Please try again.");
        }

        // Command Errors
        if (lowerError.Contains("unknown command") || lowerError.Contains("invalid command"))
        {
            return ("Unknown Command",
                "The glove does not recognize this command.\n\n" +
                "This may indicate a firmware version mismatch. Consider updating the glove firmware.");
        }

        // Generic Error
        return ("Error", errorMessage);
    }

    /// <summary>
    /// Gets a user-friendly title for an exception.
    /// </summary>
    public static string GetErrorTitle(Exception ex)
    {
        return ex switch
        {
            BlueBuzzahCommandException => "Command Error",
            TimeoutException => "Timeout",
            InvalidOperationException => "Invalid Operation",
            _ => "Error"
        };
    }

    /// <summary>
    /// Gets a user-friendly message for an exception.
    /// </summary>
    public static string GetErrorMessage(Exception ex)
    {
        if (ex is BlueBuzzahCommandException commandEx)
        {
            var (_, message) = GetFriendlyError(commandEx.Message);
            return message;
        }

        if (ex is TimeoutException)
        {
            return "The operation took too long to complete.\n\n" +
                   "Please check the Bluetooth connection and try again.";
        }

        if (ex is InvalidOperationException)
        {
            return ex.Message;
        }

        return $"An unexpected error occurred:\n\n{ex.Message}";
    }
}
