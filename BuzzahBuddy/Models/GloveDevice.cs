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
    /// Gets or sets the left glove battery voltage.
    /// Range: 3.0V (empty) to 4.2V (full).
    /// </summary>
    public double BatteryLeftVoltage { get; set; }

    /// <summary>
    /// Gets or sets the right glove battery voltage.
    /// Range: 3.0V (empty) to 4.2V (full).
    /// </summary>
    public double BatteryRightVoltage { get; set; }

    /// <summary>
    /// Gets the estimated battery percentage for the left glove (0-100).
    /// Calculated from voltage: 3.0V = 0%, 4.2V = 100%.
    /// </summary>
    public int BatteryLeftPercentage => VoltageToPercentage(BatteryLeftVoltage);

    /// <summary>
    /// Gets the estimated battery percentage for the right glove (0-100).
    /// Calculated from voltage: 3.0V = 0%, 4.2V = 100%.
    /// </summary>
    public int BatteryRightPercentage => VoltageToPercentage(BatteryRightVoltage);

    /// <summary>
    /// Gets the minimum battery percentage of both gloves.
    /// </summary>
    public int BatteryLevel => Math.Min(BatteryLeftPercentage, BatteryRightPercentage);

    /// <summary>
    /// Gets the battery status color for the left glove based on voltage.
    /// Green: &gt;3.6V, Yellow: 3.3-3.6V, Red: &lt;3.3V
    /// </summary>
    public string BatteryLeftColor => GetBatteryColor(BatteryLeftVoltage);

    /// <summary>
    /// Gets the battery status color for the right glove based on voltage.
    /// Green: &gt;3.6V, Yellow: 3.3-3.6V, Red: &lt;3.3V
    /// </summary>
    public string BatteryRightColor => GetBatteryColor(BatteryRightVoltage);

    /// <summary>
    /// Gets or sets the current connection state of the device.
    /// </summary>
    public ConnectionState ConnectionState { get; set; } = ConnectionState.Disconnected;

    /// <summary>
    /// Gets or sets the firmware version of the device.
    /// </summary>
    public string? FirmwareVersion { get; set; }

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

    /// <summary>
    /// Converts battery voltage to percentage estimate.
    /// </summary>
    /// <param name="voltage">Battery voltage (3.0-4.2V)</param>
    /// <returns>Percentage (0-100)</returns>
    private static int VoltageToPercentage(double voltage)
    {
        const double minVoltage = 3.0;
        const double maxVoltage = 4.2;

        if (voltage <= minVoltage) return 0;
        if (voltage >= maxVoltage) return 100;

        return (int)((voltage - minVoltage) / (maxVoltage - minVoltage) * 100);
    }

    /// <summary>
    /// Gets the battery status color based on voltage thresholds.
    /// </summary>
    /// <param name="voltage">Battery voltage</param>
    /// <returns>Color name: "Green", "Yellow", or "Red"</returns>
    private static string GetBatteryColor(double voltage)
    {
        if (voltage > 3.6) return "Green";
        if (voltage >= 3.3) return "Yellow";
        return "Red";
    }
}
