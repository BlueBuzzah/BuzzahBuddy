namespace BuzzahBuddy.Models;

/// <summary>
/// Represents settings for calibration mode finger testing.
/// Finger indices follow the firmware map (config.h): 0=Index, 1=Middle,
/// 2=Ring, 3=Pinky, 4=Thumb (5-motor boards only). Indices at and above the
/// per-glove actuator count address the same fingers on the secondary glove.
/// </summary>
public class CalibrationSettings
{
    private static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Pinky", "Thumb" };

    /// <summary>
    /// Finger index (0 to 2*ActuatorsPerGlove-1).
    /// </summary>
    public int FingerIndex { get; set; }

    /// <summary>
    /// Motors per glove: 4 (BlueBuzzah) or 5 (PentaBuzzer).
    /// </summary>
    public int ActuatorsPerGlove { get; set; } = 4;

    /// <summary>
    /// Vibration intensity percentage (0-100).
    /// </summary>
    public int Intensity { get; set; } = 80;

    /// <summary>
    /// Vibration duration in milliseconds (50-2000).
    /// </summary>
    public int DurationMs { get; set; } = 500;

    /// <summary>
    /// Gets whether this finger is on the primary glove.
    /// </summary>
    public bool IsPrimaryDevice => FingerIndex >= 0 && FingerIndex < ActuatorsPerGlove;

    /// <summary>
    /// Gets whether this finger is on the secondary glove.
    /// </summary>
    public bool IsSecondaryDevice => FingerIndex >= ActuatorsPerGlove && FingerIndex < 2 * ActuatorsPerGlove;

    /// <summary>
    /// Gets the display name for the finger (e.g. "Primary Index").
    /// </summary>
    public string FingerName => GetFingerLabel(FingerIndex, ActuatorsPerGlove);

    /// <summary>
    /// Maps a protocol finger index to its display label, per the firmware finger map.
    /// </summary>
    public static string GetFingerLabel(int fingerIndex, int actuatorsPerGlove)
    {
        var glove = fingerIndex < actuatorsPerGlove ? "Primary" : "Secondary";
        var local = fingerIndex % actuatorsPerGlove;
        if (fingerIndex < 0 || fingerIndex >= 2 * actuatorsPerGlove || local >= FingerNames.Length)
        {
            return $"Finger {fingerIndex}";
        }
        return $"{glove} {FingerNames[local]}";
    }

    /// <summary>
    /// Creates calibration settings for every finger on both gloves.
    /// </summary>
    public static List<CalibrationSettings> CreateAll(int actuatorsPerGlove = 4)
    {
        var settings = new List<CalibrationSettings>();
        for (int i = 0; i < 2 * actuatorsPerGlove; i++)
        {
            settings.Add(new CalibrationSettings { FingerIndex = i, ActuatorsPerGlove = actuatorsPerGlove });
        }
        return settings;
    }
}
