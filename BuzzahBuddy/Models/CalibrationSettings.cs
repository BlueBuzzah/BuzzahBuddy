namespace BuzzahBuddy.Models;

/// <summary>
/// Represents settings for calibration mode finger testing.
/// </summary>
public class CalibrationSettings
{
    /// <summary>
    /// Finger index (0-7).
    /// 0-3: Left glove (VL) - Thumb, Index, Middle, Ring
    /// 4-7: Right glove (VR) - Thumb, Index, Middle, Ring
    /// </summary>
    public int FingerIndex { get; set; }

    /// <summary>
    /// Vibration intensity percentage (0-100).
    /// </summary>
    public int Intensity { get; set; } = 80;

    /// <summary>
    /// Vibration duration in milliseconds (50-2000).
    /// </summary>
    public int DurationMs { get; set; } = 500;

    /// <summary>
    /// Gets whether this is a left hand finger (0-3).
    /// </summary>
    public bool IsLeftHand => FingerIndex >= 0 && FingerIndex <= 3;

    /// <summary>
    /// Gets whether this is a right hand finger (4-7).
    /// </summary>
    public bool IsRightHand => FingerIndex >= 4 && FingerIndex <= 7;

    /// <summary>
    /// Gets the display name for the finger.
    /// </summary>
    public string FingerName
    {
        get
        {
            return FingerIndex switch
            {
                0 => "Left Thumb",
                1 => "Left Index",
                2 => "Left Middle",
                3 => "Left Ring",
                4 => "Right Thumb",
                5 => "Right Index",
                6 => "Right Middle",
                7 => "Right Ring",
                _ => $"Finger {FingerIndex}"
            };
        }
    }

    /// <summary>
    /// Gets the short name for the finger (for compact UI).
    /// </summary>
    public string FingerShortName
    {
        get
        {
            return FingerIndex switch
            {
                0 => "L-Thumb",
                1 => "L-Index",
                2 => "L-Middle",
                3 => "L-Ring",
                4 => "R-Thumb",
                5 => "R-Index",
                6 => "R-Middle",
                7 => "R-Ring",
                _ => $"F{FingerIndex}"
            };
        }
    }

    /// <summary>
    /// Creates calibration settings for all 8 fingers.
    /// </summary>
    /// <returns>List of 8 CalibrationSettings objects</returns>
    public static List<CalibrationSettings> CreateAll()
    {
        var settings = new List<CalibrationSettings>();
        for (int i = 0; i < 8; i++)
        {
            settings.Add(new CalibrationSettings { FingerIndex = i });
        }
        return settings;
    }
}
