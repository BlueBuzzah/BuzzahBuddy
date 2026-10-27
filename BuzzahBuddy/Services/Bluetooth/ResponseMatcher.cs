using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// Decides whether a received frame answers the command awaiting a reply. The
/// protocol has no correlation IDs, so a late frame from a timed-out command would
/// otherwise complete the current command with the wrong response and shift every
/// later response by one (permanent desync).
/// </summary>
/// <remarks>
/// Current firmware begins every reply with <c>CMD:&lt;COMMAND&gt;</c>, which is an
/// exact match. Older firmware has no echo, so the fallback is a key the reply must
/// contain; commands absent from that map accept any frame, and error frames match
/// any command.
/// </remarks>
public static class ResponseMatcher
{
    private static readonly Dictionary<string, string> ExpectedResponseKeys = new()
    {
        ["INFO"] = "ROLE",
        ["BATTERY"] = "BATP",
        ["PING"] = "PONG",
        ["PROFILE_LIST"] = "PROFILE",
        ["PROFILE_GET"] = "TYPE",
        ["PROFILE_LOAD"] = "STATUS",
        ["SESSION_START"] = "SESSION_STATUS",
        ["SESSION_PAUSE"] = "SESSION_STATUS",
        ["SESSION_RESUME"] = "SESSION_STATUS",
        ["SESSION_STOP"] = "SESSION_STATUS",
        ["SESSION_STATUS"] = "SESSION_STATUS",
        ["THERAPY_LED_OFF"] = "THERAPY_LED_OFF",
    };

    /// <summary>
    /// The command name the firmware echoes in its <c>CMD:</c> line: everything before
    /// the first ':' (<c>PROFILE_LOAD:3</c> → <c>PROFILE_LOAD</c>), uppercased.
    /// </summary>
    public static string CommandName(string command) => command.Split(':')[0].ToUpperInvariant();

    /// <summary>
    /// True when <paramref name="response"/> completes the pending command
    /// <paramref name="pendingCommandName"/>; false means it is stale and must be dropped.
    /// </summary>
    public static bool Completes(string pendingCommandName, CommandResponse response)
    {
        var echoed = response.GetString("CMD");
        if (echoed != null)
        {
            return string.Equals(echoed, pendingCommandName, StringComparison.OrdinalIgnoreCase);
        }

        if (response.IsError)
        {
            return true;
        }

        return !ExpectedResponseKeys.TryGetValue(pendingCommandName, out var key) ||
               response.GetString(key) != null;
    }
}
