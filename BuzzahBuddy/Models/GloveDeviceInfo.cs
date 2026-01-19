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
    /// Primary device battery voltage.
    /// </summary>
    public double BatteryPrimaryVoltage { get; set; }

    /// <summary>
    /// Secondary device battery voltage.
    /// </summary>
    public double BatterySecondaryVoltage { get; set; }

    /// <summary>
    /// Current session status (IDLE, RUNNING, PAUSED).
    /// </summary>
    public string Status { get; set; } = "IDLE";

    /// <summary>
    /// Parses a CommandResponse into a GloveDeviceInfo object.
    /// </summary>
    /// <param name="response">The response from INFO command</param>
    /// <returns>Parsed GloveDeviceInfo</returns>
    public static GloveDeviceInfo FromCommandResponse(CommandResponse response)
    {
        return new GloveDeviceInfo
        {
            Role = response.GetString("ROLE") ?? "UNKNOWN",
            Name = response.GetString("NAME") ?? "UNKNOWN",
            FirmwareVersion = response.GetString("FW") ?? "0.0.0",
            // Per BLE protocol v2.0.0: Battery keys are BATP and BATS
            BatteryPrimaryVoltage = response.GetDouble("BATP") ?? 0.0,
            BatterySecondaryVoltage = response.GetDouble("BATS") ?? 0.0,
            Status = response.GetString("STATUS") ?? "IDLE"
        };
    }
}
