using System.Globalization;
using BuzzahBuddy.Services.Bluetooth;
// Explicit for the plain net9.0 test build, which lacks MAUI implicit usings
using Microsoft.Maui.Graphics;

namespace BuzzahBuddy.Models;

/// <summary>
/// Battery reading interpretation shared by services and view models.
/// A null voltage means "no reading available" — the firmware reports
/// BATP/BATS as 0.00 when the monitor failed or the secondary timed out,
/// and a real LiPo pack never reads at or below 0V.
/// </summary>
public static class BatteryReading
{
    private const double MinVoltage = 3.0;
    private const double MaxVoltage = 4.2;

    /// <summary>
    /// Maps a raw protocol value to a usable reading: null for a missing,
    /// unparsable, or sentinel (≤ 0) value.
    /// </summary>
    public static double? FromRaw(double? raw) =>
        raw is > 0.0 ? raw : null;

    /// <summary>
    /// Linear voltage-to-percentage mapping: 3.0V = 0%, 4.2V = 100%.
    /// </summary>
    public static int ToPercentage(double voltage)
    {
        if (voltage <= MinVoltage) return 0;
        if (voltage >= MaxVoltage) return 100;
        return (int)((voltage - MinVoltage) / (MaxVoltage - MinVoltage) * 100);
    }

    /// <summary>
    /// Display string for a reading, e.g. "60% (3.72V)", or "—" when unavailable.
    /// </summary>
    public static string Format(double? voltage) =>
        voltage is { } v
            ? string.Create(CultureInfo.InvariantCulture, $"{ToPercentage(v)}% ({v:F2}V)")
            : "—";

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
