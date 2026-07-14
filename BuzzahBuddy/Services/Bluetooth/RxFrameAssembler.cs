using System.Text;

namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// Reassembles EOT-terminated response frames from BLE notification packets.
/// A frame may span multiple packets, and one packet may carry the tail of one
/// frame and the head of the next.
/// </summary>
public class RxFrameAssembler
{
    private const int MaxPartialFrameLength = 4096;
    private readonly StringBuilder _buffer = new();

    // Firmware-internal glove↔glove message prefixes (firmware menu_controller.cpp
    // INTERNAL_MESSAGES). Current firmware writes per connection handle, so a
    // PHONE-typed connection never receives these; older firmware notified every
    // subscriber, and one of these landing here would be glued onto the front of
    // the next real frame. Internal messages carry no EOT and fit one packet, so a
    // packet is dropped only when it starts a new frame, has no EOT, and starts
    // with a known internal prefix. Only response key sharing a prefix is "PONG:",
    // which always arrives EOT-terminated in a single packet, so it is never dropped.
    private static readonly string[] InternalMessagePrefixes =
    {
        "BUZZ", "PING", "PONG", "PARAM_UPDATE", "SEED", "GET_BATTERY",
        "BATRESPONSE", "ACK_PARAM_UPDATE", "SYNC_", "FIRST_SYNC", "ACK_SYNC",
        "START_SESSION", "PAUSE_SESSION", "RESUME_SESSION", "STOP_SESSION",
        "IDENTIFY:", "LED_OFF_SYNC", "DEBUG_FLASH", "DEBUG_SYNC", "MC:", "MC_ACK:",
        "CALIB_BUZZ:", "CALIB_STOP",
        "EXECUTE_BUZZ"   // legacy firmware sync message, not in current INTERNAL_MESSAGES
    };

    /// <summary>
    /// Appends incoming packet text and returns each complete EOT-terminated
    /// frame (EOT included), in order. Retains any trailing partial frame.
    /// Frame-initial packets that match a firmware-internal message prefix and
    /// carry no EOT are dropped rather than buffered.
    /// </summary>
    public IReadOnlyList<string> Append(string text)
    {
        if (_buffer.Length == 0 &&
            !text.Contains(BlueBuzzahConstants.EndOfTransmission) &&
            InternalMessagePrefixes.Any(p => text.StartsWith(p, StringComparison.Ordinal)))
        {
            return Array.Empty<string>();
        }

        _buffer.Append(text);
        var buffered = _buffer.ToString();
        var frames = new List<string>();
        int eotIndex;
        while ((eotIndex = buffered.IndexOf(BlueBuzzahConstants.EndOfTransmission)) >= 0)
        {
            frames.Add(buffered[..(eotIndex + 1)]);
            buffered = buffered[(eotIndex + 1)..];
        }
        _buffer.Clear();
        // ponytail: cap discards a runaway partial frame rather than growing unbounded
        if (buffered.Length < MaxPartialFrameLength)
        {
            _buffer.Append(buffered);
        }
        return frames;
    }

    /// <summary>
    /// True when a partial (not yet EOT-terminated) frame is buffered.
    /// </summary>
    public bool HasPartialFrame => _buffer.Length > 0;

    public void Reset() => _buffer.Clear();
}
