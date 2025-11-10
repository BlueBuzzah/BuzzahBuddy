namespace BuzzahBuddy.Models;

/// <summary>
/// Represents a therapy profile for BlueBuzzah vibrotactile gloves.
/// Profiles define the timing, amplitude, and pattern characteristics for therapy sessions.
/// </summary>
public class TherapyProfile
{
    /// <summary>
    /// Profile ID (1-3 for preset profiles, 0 for custom).
    /// 1 = Regular VCR, 2 = Noisy VCR (default), 3 = Hybrid VCR
    /// </summary>
    public int ProfileId { get; set; }

    /// <summary>
    /// Display name of the profile.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Actuator type (typically "LRA" for Linear Resonant Actuator).
    /// </summary>
    public string ActuatorType { get; set; } = "LRA";

    /// <summary>
    /// Actuator resonant frequency in Hz (range: 150-300).
    /// </summary>
    public int ActuatorFrequency { get; set; }

    /// <summary>
    /// Actuator drive voltage in volts (range: 1.0-3.3).
    /// </summary>
    public double ActuatorVoltage { get; set; }

    /// <summary>
    /// Vibration ON duration in seconds (range: 0.050-0.500).
    /// </summary>
    public double TimeOn { get; set; }

    /// <summary>
    /// Vibration OFF duration in seconds (range: 0.020-0.200).
    /// </summary>
    public double TimeOff { get; set; }

    /// <summary>
    /// Total session duration in minutes (range: 1-180).
    /// </summary>
    public int TimeSession { get; set; }

    /// <summary>
    /// Minimum amplitude percentage (range: 0-100).
    /// </summary>
    public int AmplitudeMin { get; set; }

    /// <summary>
    /// Maximum amplitude percentage (range: 0-100).
    /// </summary>
    public int AmplitudeMax { get; set; }

    /// <summary>
    /// Jitter percentage for timing variation (range: 0-50).
    /// Adds randomness to vibration patterns.
    /// </summary>
    public double Jitter { get; set; }

    /// <summary>
    /// Whether patterns are mirrored between left and right hands.
    /// </summary>
    public bool Mirror { get; set; }

    /// <summary>
    /// Pattern type identifier (e.g., "RNDP" for random pattern).
    /// </summary>
    public string PatternType { get; set; } = "RNDP";

    /// <summary>
    /// Description of the profile for display purposes.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is a custom (user-modified) profile.
    /// </summary>
    public bool IsCustom => ProfileId == 0;

    /// <summary>
    /// Creates the three predefined therapy profiles.
    /// </summary>
    /// <returns>List of preset profiles</returns>
    public static List<TherapyProfile> GetPresetProfiles()
    {
        return new List<TherapyProfile>
        {
            // Profile 1: Regular VCR
            new TherapyProfile
            {
                ProfileId = 1,
                Name = "Regular VCR",
                Description = "Standard vibrotactile continuous reset pattern with consistent timing",
                ActuatorType = "LRA",
                ActuatorFrequency = 250,
                ActuatorVoltage = 2.5,
                TimeOn = 0.100,
                TimeOff = 0.067,
                TimeSession = 120,
                AmplitudeMin = 100,
                AmplitudeMax = 100,
                Jitter = 0,
                Mirror = false,
                PatternType = "RNDP"
            },

            // Profile 2: Noisy VCR (Default)
            new TherapyProfile
            {
                ProfileId = 2,
                Name = "Noisy VCR",
                Description = "VCR pattern with jitter and mirrored patterns - recommended default",
                ActuatorType = "LRA",
                ActuatorFrequency = 250,
                ActuatorVoltage = 2.5,
                TimeOn = 0.100,
                TimeOff = 0.067,
                TimeSession = 120,
                AmplitudeMin = 100,
                AmplitudeMax = 100,
                Jitter = 23.5,
                Mirror = true,
                PatternType = "RNDP"
            },

            // Profile 3: Hybrid VCR
            new TherapyProfile
            {
                ProfileId = 3,
                Name = "Hybrid VCR",
                Description = "Mixed frequency stimulation pattern",
                ActuatorType = "LRA",
                ActuatorFrequency = 250,
                ActuatorVoltage = 2.5,
                TimeOn = 0.100,
                TimeOff = 0.067,
                TimeSession = 120,
                AmplitudeMin = 100,
                AmplitudeMax = 100,
                Jitter = 10.0,
                Mirror = false,
                PatternType = "RNDP"
            }
        };
    }

    /// <summary>
    /// Creates a copy of this profile for customization.
    /// </summary>
    /// <returns>A new custom profile with copied values</returns>
    public TherapyProfile ToCustomProfile()
    {
        return new TherapyProfile
        {
            ProfileId = 0,
            Name = $"Custom ({Name})",
            Description = "User-customized therapy profile",
            ActuatorType = ActuatorType,
            ActuatorFrequency = ActuatorFrequency,
            ActuatorVoltage = ActuatorVoltage,
            TimeOn = TimeOn,
            TimeOff = TimeOff,
            TimeSession = TimeSession,
            AmplitudeMin = AmplitudeMin,
            AmplitudeMax = AmplitudeMax,
            Jitter = Jitter,
            Mirror = Mirror,
            PatternType = PatternType
        };
    }
}
