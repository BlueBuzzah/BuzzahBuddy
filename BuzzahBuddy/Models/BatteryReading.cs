using System.Globalization;

namespace BuzzahBuddy.Models;

/// <summary>
/// Battery reading interpretation shared by services and view models.
/// A null voltage means "no reading available" — the firmware reports
/// BATP/BATS as 0.00 when the monitor failed or the secondary timed out,
/// and a real LiPo pack never reads at or below 0V.
/// </summary>
public static class BatteryReading
{
    // Firmware's LiPo discharge curve (src/hardware.cpp VOLTAGE_CURVE): {volts, percent}.
    private static readonly (double Volts, double Percent)[] Curve =
    [
        (4.20, 100), (4.15, 95), (4.11, 90), (4.08, 85), (4.02, 80),
        (3.98, 75), (3.95, 70), (3.91, 65), (3.87, 60), (3.85, 55),
        (3.84, 50), (3.82, 45), (3.80, 40), (3.79, 35), (3.77, 30),
        (3.75, 25), (3.73, 20), (3.71, 15), (3.69, 10), (3.61, 5),
        (3.27, 0),
    ];

    /// <summary>
    /// Below this a glove is low: it can't finish a 2-hour session, shows an orange LED and
    /// refuses SESSION_START. Mirrors firmware board_config.h BATTERY_LOW_VOLTAGE, which is
    /// provisional until bench calibration; change both together.
    /// </summary>
    public const double LowVoltage = 3.80;

    /// <summary><see cref="LowVoltage"/> as a percentage (40 on the curve).</summary>
    public static readonly int LowPercent = ToPercentage(LowVoltage);

    /// <summary>Below <see cref="LowVoltage"/>, like the firmware; null (no reading) is never low.</summary>
    public static bool IsLow(double? voltage) => voltage is < LowVoltage;

    /// <summary>
    /// Maps a raw protocol value to a usable reading: null for a missing,
    /// unparsable, or sentinel (≤ 0) value.
    /// </summary>
    public static double? FromRaw(double? raw) =>
        raw is > 0.0 ? raw : null;

    /// <summary>
    /// Voltage to percentage on the firmware's curve, interpolated and truncated the same way,
    /// so the app shows the percentage the glove computes.
    /// </summary>
    public static int ToPercentage(double voltage)
    {
        if (voltage >= Curve[0].Volts) return 100;
        for (var i = 1; i < Curve.Length; i++)
        {
            var (v2, p2) = Curve[i];
            if (voltage > v2)
            {
                var (v1, p1) = Curve[i - 1];
                return (int)(p2 + (voltage - v2) / (v1 - v2) * (p1 - p2));
            }
        }
        return 0;
    }

    /// <summary>
    /// Display string for a reading, e.g. "60%", or "—" when unavailable.
    /// </summary>
    public static string Format(double? voltage) =>
        voltage is { } v ? string.Create(CultureInfo.InvariantCulture, $"{ToPercentage(v)}%") : "—";

    /// <summary>The raw voltage for technical detail, e.g. "3.72V"; null when unavailable.</summary>
    public static string? FormatVolts(double? voltage) =>
        voltage is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v:F2}V") : null;
}
