using BuzzahBuddy.Services.Bluetooth;

namespace BuzzahBuddy.Helpers;

/// <summary>
/// Shared battery utility methods for voltage-to-percentage conversion and status colors.
/// </summary>
public static class BatteryHelper
{
    public const double MinVoltage = 3.0;
    public const double MaxVoltage = 4.2;

    /// <summary>
    /// Converts battery voltage to percentage (0-100) via linear interpolation.
    /// </summary>
    public static int VoltageToPercentage(double voltage)
    {
        if (voltage <= MinVoltage) return 0;
        if (voltage >= MaxVoltage) return 100;
        return (int)((voltage - MinVoltage) / (MaxVoltage - MinVoltage) * 100);
    }

    /// <summary>
    /// Gets display color for a battery percentage. Green >= 60%, Orange >= 20%, Red below.
    /// </summary>
    public static Color GetBatteryColor(int percentage) => percentage switch
    {
        >= 60 => Colors.Green,
        >= 20 => Colors.Orange,
        _ => Colors.Red
    };

    /// <summary>
    /// Gets display color based on battery voltage using BlueBuzzahConstants thresholds.
    /// </summary>
    public static Color GetBatteryColorFromVoltage(double voltage)
    {
        if (voltage > BlueBuzzahConstants.BatteryGoodThreshold) return Colors.Green;
        if (voltage >= BlueBuzzahConstants.BatteryMediumThreshold) return Colors.Orange;
        return Colors.Red;
    }

    /// <summary>
    /// Gets human-readable status text for a battery voltage level.
    /// </summary>
    public static string GetBatteryStatusText(double voltage)
    {
        if (voltage > BlueBuzzahConstants.BatteryGoodThreshold) return "good";
        if (voltage >= BlueBuzzahConstants.BatteryMediumThreshold) return "low";
        return "critical";
    }
}
