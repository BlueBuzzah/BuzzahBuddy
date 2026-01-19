namespace BuzzahBuddy.Models;

/// <summary>
/// Represents settings for calibration mode finger testing.
/// </summary>
public class CalibrationSettings
{
    /// <summary>
    /// Finger index (0-7).
    /// 0-3: Primary device - Thumb, Index, Middle, Ring
    /// 4-7: Secondary device - Thumb, Index, Middle, Ring
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
    /// Gets whether this is a Primary device finger (0-3).
    /// </summary>
    public bool IsPrimaryDevice => FingerIndex >= 0 && FingerIndex <= 3;

    /// <summary>
    /// Gets whether this is a Secondary device finger (4-7).
    /// </summary>
    public bool IsSecondaryDevice => FingerIndex >= 4 && FingerIndex <= 7;

    /// <summary>
    /// Gets the display name for the finger.
    /// </summary>
    public string FingerName
    {
        get
        {
            return FingerIndex switch
            {
                0 => "Primary Thumb",
                1 => "Primary Index",
                2 => "Primary Middle",
                3 => "Primary Ring",
                4 => "Secondary Thumb",
                5 => "Secondary Index",
                6 => "Secondary Middle",
                7 => "Secondary Ring",
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
                0 => "P-Thumb",
                1 => "P-Index",
                2 => "P-Middle",
                3 => "P-Ring",
                4 => "S-Thumb",
                5 => "S-Index",
                6 => "S-Middle",
                7 => "S-Ring",
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
