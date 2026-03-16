namespace BuzzahBuddy.Models;

public enum ScanOutcome
{
    DevicesFound,
    NoDevicesFound,
    ScanFailed
}

public record ScanResult(
    ScanOutcome Outcome,
    IEnumerable<GloveDevice> Devices,
    string? ErrorMessage = null
);
