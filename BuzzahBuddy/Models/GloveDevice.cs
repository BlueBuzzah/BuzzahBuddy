namespace BuzzahBuddy.Models;

/// <summary>
/// Represents a BlueBuzzah glove device discovered via Bluetooth.
/// </summary>
public class GloveDevice
{
    /// <summary>
    /// Gets or sets the unique identifier for the device.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the device.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MAC address of the device.
    /// </summary>
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current connection state of the device.
    /// </summary>
    public ConnectionState ConnectionState { get; set; } = ConnectionState.Disconnected;

    /// <summary>
    /// Gets or sets the firmware version of the device.
    /// </summary>
    public string? FirmwareVersion { get; set; }

    /// <summary>
    /// Hardware generation ("v2" or "v3"), parsed from the BLE advertisement's
    /// manufacturer data. Null when the firmware predates hardware-version
    /// advertising.
    /// </summary>
    public string? HardwareVersion { get; set; }

    /// <summary>
    /// Name with the hardware generation appended (e.g. "BlueBuzzah v3"),
    /// or the plain name when the generation is unknown.
    /// </summary>
    public string DisplayName => HardwareVersion == null ? Name : $"{Name} {HardwareVersion}";

    /// <summary>
    /// Gets or sets the signal strength (RSSI) of the Bluetooth connection.
    /// </summary>
    public int SignalStrength { get; set; }

    /// <summary>
    /// Gets a user-friendly signal strength display.
    /// Converts RSSI to Strong/Medium/Weak labels.
    /// </summary>
    public string SignalStrengthDisplay
    {
        get
        {
            // RSSI values typically range from -30 (excellent) to -90 (poor)
            if (SignalStrength >= -50)
                return "Signal: Strong";
            if (SignalStrength >= -70)
                return "Signal: Medium";
            return "Signal: Weak";
        }
    }

    /// <summary>
    /// Gets or sets the last time the device was successfully connected.
    /// </summary>
    public DateTime? LastConnected { get; set; }

}
