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

    /// <summary>
    /// Appends incoming packet text and returns each complete EOT-terminated
    /// frame (EOT included), in order. Retains any trailing partial frame.
    /// </summary>
    public IReadOnlyList<string> Append(string text)
    {
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

    public void Reset() => _buffer.Clear();
}
