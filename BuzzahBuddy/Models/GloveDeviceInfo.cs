namespace BuzzahBuddy.Models;

/// <summary>
/// Glove device information returned by the INFO command.
/// Renamed to avoid conflict with Microsoft.Maui.Devices.DeviceInfo.
/// </summary>
public class GloveDeviceInfo
{
    /// <summary>
    /// Device role (PRIMARY or SECONDARY).
    /// App connects only to PRIMARY device.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Device name (e.g., "BlueBuzzah-Primary" or "BlueBuzzah-Secondary").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Firmware version (e.g., "1.0.0").
    /// </summary>
    public string FirmwareVersion { get; set; } = string.Empty;

    /// <summary>
    /// Primary device battery voltage; null when no reading is available.
    /// </summary>
    public double? BatteryPrimaryVoltage { get; set; }

    /// <summary>
    /// Secondary device battery voltage; null when no reading is available.
    /// </summary>
    public double? BatterySecondaryVoltage { get; set; }

    /// <summary>
    /// Current session status (IDLE, RUNNING, PAUSED).
    /// </summary>
    public string Status { get; set; } = "IDLE";

    /// <summary>
    /// Motors per glove: 4 (BlueBuzzah) or 5 (PentaBuzzer). Firmware ≥2.1 reports
    /// MOTORS in INFO; older firmware defaults to 4.
    /// </summary>
    public int Motors { get; set; } = 4;

    /// <summary>
    /// Active profile ID (1-6), 0 if the firmware didn't report one.
    /// </summary>
    public int ProfileId { get; set; }

    /// <summary>
    /// Active profile name as stored on the device (e.g. "noisy_vcr").
    /// </summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>
    /// Parses a CommandResponse into a GloveDeviceInfo object.
    /// </summary>
    /// <param name="response">The response from INFO command</param>
    /// <returns>Parsed GloveDeviceInfo</returns>
    public static GloveDeviceInfo FromCommandResponse(CommandResponse response)
    {
        var info = new GloveDeviceInfo
        {
            Role = response.GetString("ROLE") ?? "UNKNOWN",
            Name = response.GetString("NAME") ?? "UNKNOWN",
            FirmwareVersion = response.GetString("FW") ?? "0.0.0",
            // Per BLE protocol v2.0.0: Battery keys are BATP and BATS.
            // Missing keys and the firmware's 0.00 sentinel both mean "no reading".
            BatteryPrimaryVoltage = BatteryReading.FromRaw(response.GetDouble("BATP")),
            BatterySecondaryVoltage = BatteryReading.FromRaw(response.GetDouble("BATS")),
            Status = response.GetString("STATUS") ?? "IDLE"
        };

        info.Motors = response.GetInt("MOTORS") ?? 4;
        var profileValue = response.GetString("PROFILE");   // "2:noisy_vcr"
        if (profileValue != null)
        {
            var parts = profileValue.Split(':', 2);
            if (int.TryParse(parts[0], out var pid))
            {
                info.ProfileId = pid;
                info.ProfileName = parts.Length > 1 ? parts[1] : string.Empty;
            }
        }

        return info;
    }
}
