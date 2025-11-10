namespace BuzzahBuddy.Models;

/// <summary>
/// Glove device information returned by the INFO command.
/// Renamed to avoid conflict with Microsoft.Maui.Devices.DeviceInfo.
/// </summary>
public class GloveDeviceInfo
{
    /// <summary>
    /// Device role (PRIMARY for VL, SECONDARY for VR).
    /// App connects only to PRIMARY (VL).
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Device name (e.g., "VL" for left glove, "VR" for right glove).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Firmware version (e.g., "1.0.0").
    /// </summary>
    public string FirmwareVersion { get; set; } = string.Empty;

    /// <summary>
    /// Left glove battery voltage.
    /// </summary>
    public double BatteryLeftVoltage { get; set; }

    /// <summary>
    /// Right glove battery voltage.
    /// </summary>
    public double BatteryRightVoltage { get; set; }

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
            BatteryLeftVoltage = response.GetDouble("BAT_LEFT") ?? 0.0,
            BatteryRightVoltage = response.GetDouble("BAT_RIGHT") ?? 0.0,
            Status = response.GetString("STATUS") ?? "IDLE"
        };
    }
}
