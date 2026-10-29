namespace BuzzahBuddy.Models;

/// <summary>
/// Represents a therapy profile for BlueBuzzah vibrotactile gloves.
/// Profiles define the timing, amplitude, and pattern characteristics for therapy sessions.
/// </summary>
public class TherapyProfile
{
    /// <summary>
    /// Profile ID of the user-editable Custom slot (custom_vcr) in the firmware's
    /// profile table. The only profile whose parameters may be edited.
    /// </summary>
    public const int CustomProfileId = 4;

    /// <summary>
    /// Profile ID (1-6 for preset profiles).
    /// Per BLE protocol v2.0.0:
    /// 1 = regular_vcr, 2 = noisy_vcr (default), 3 = hybrid_vcr,
    /// 4 = custom_vcr, 5 = gentle, 6 = quick_test
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
    /// Total session duration in minutes (range: 1-240).
    /// Per BLE protocol v2.0.0: SESSION parameter range is 1-240 minutes.
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
    /// Whether patterns are mirrored between Primary and Secondary devices.
    /// </summary>
    public bool Mirror { get; set; }

    /// <summary>
    /// Pattern type identifier (e.g., "RNDP" for random pattern, "SEQ" for sequential, "MIRRORED").
    /// Per BLE protocol v2.0.0: Valid values are RNDP, SEQ, MIRRORED.
    /// </summary>
    public string PatternType { get; set; } = "RNDP";

    /// <summary>
    /// Number of active fingers for therapy (range: 1-4).
    /// Per BLE protocol v2.0.0: FINGERS parameter controls how many fingers are active.
    /// </summary>
    public int Fingers { get; set; } = 4;

    /// <summary>
    /// Description of the profile for display purposes.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the user-editable Custom profile.
    /// </summary>
    public bool IsCustom => ProfileId == CustomProfileId;

    /// <summary>
    /// Creates the predefined therapy profiles, in display order (Custom last).
    /// </summary>
    /// <returns>List of preset profiles</returns>
    public static List<TherapyProfile> GetPresetProfiles()
    {
        return new List<TherapyProfile>
        {
            // Profile 1: Regular
            new TherapyProfile
            {
                ProfileId = 1,
                Name = "Regular",
                Description = "Steady timing. Each glove uses its own random finger order.",
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
                PatternType = "RNDP",
                Fingers = 4
            },

            // Profile 2: Noisy (Default)
            new TherapyProfile
            {
                ProfileId = 2,
                Name = "Noisy",
                Description = "Slightly varied timing. Both gloves use the same finger order.",
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
                PatternType = "RNDP",
                Fingers = 4
            },

            // Profile 3: Hybrid
            new TherapyProfile
            {
                ProfileId = 3,
                Name = "Hybrid",
                Description = "Slightly varied timing. Each glove uses its own finger order.",
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
                PatternType = "RNDP",
                Fingers = 4
            },

            // Profile 5: Gentle (per BLE protocol v2.0.0)
            new TherapyProfile
            {
                ProfileId = 5,
                Name = "Gentle",
                Description = "Softer buzzes in a fixed finger order. 1-hour sessions.",
                ActuatorType = "LRA",
                ActuatorFrequency = 250,
                ActuatorVoltage = 2.0,
                TimeOn = 0.100,
                TimeOff = 0.067,
                TimeSession = 120,
                AmplitudeMin = 30,
                AmplitudeMax = 60,
                Jitter = 0,
                Mirror = false,
                PatternType = "SEQ",
                Fingers = 4
            },

            // Profile 6: Quick Test (per BLE protocol v2.0.0)
            new TherapyProfile
            {
                ProfileId = 6,
                Name = "Quick Test",
                Description = "A 5-minute session to check that your gloves work.",
                ActuatorType = "LRA",
                ActuatorFrequency = 250,
                ActuatorVoltage = 2.5,
                TimeOn = 0.100,
                TimeOff = 0.067,
                TimeSession = 5,
                AmplitudeMin = 100,
                AmplitudeMax = 100,
                Jitter = 0,
                Mirror = false,
                PatternType = "RNDP",
                Fingers = 4
            },

            // Profile 4: Custom (per BLE protocol v2.0.0)
            new TherapyProfile
            {
                ProfileId = 4,
                Name = "Custom",
                Description = "Your own settings. The only profile you can edit.",
                ActuatorType = "LRA",
                ActuatorFrequency = 250,
                ActuatorVoltage = 2.5,
                TimeOn = 0.100,
                TimeOff = 0.067,
                TimeSession = 120,
                AmplitudeMin = 50,
                AmplitudeMax = 100,
                Jitter = 0,
                Mirror = false,
                PatternType = "RNDP",
                Fingers = 4
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
            ProfileId = CustomProfileId,
            Name = $"Custom ({Name})",
            Description = "Your own settings. The only profile you can edit.",
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
            PatternType = PatternType,
            Fingers = Fingers
        };
    }
}
