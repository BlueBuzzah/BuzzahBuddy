namespace BuzzahBuddy.Services.Bluetooth;

/// <summary>
/// Constants for BlueBuzzah glove Bluetooth Low Energy communication.
/// Uses Nordic UART Service (NUS) for text-based command protocol.
/// </summary>
public static class BlueBuzzahConstants
{
    /// <summary>
    /// The device name for the PRIMARY BlueBuzzah glove.
    /// App connects only to Primary; Primary relays commands to Secondary as needed.
    /// Per BLE protocol v2.0.0: Device is named "BlueBuzzah".
    /// </summary>
    public const string DeviceName = "BlueBuzzah";

    /// <summary>
    /// Nordic UART Service (NUS) UUID.
    /// Standard service for UART-style BLE communication.
    /// </summary>
    public static readonly Guid NordicUartServiceUuid = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");

    /// <summary>
    /// TX Characteristic UUID (Write - App → Glove).
    /// Used to send text commands to the glove.
    /// </summary>
    public static readonly Guid TxCharacteristicUuid = Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");

    /// <summary>
    /// RX Characteristic UUID (Notify - Glove → App).
    /// Used to receive KEY:VALUE responses from the glove.
    /// </summary>
    public static readonly Guid RxCharacteristicUuid = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");

    /// <summary>
    /// End-of-transmission character that marks the end of a response.
    /// Only messages ending with this character are app-directed responses.
    /// Internal Primary↔Secondary sync messages don't include this character and should be filtered.
    /// </summary>
    public const char EndOfTransmission = '\x04';

    /// <summary>
    /// Command terminator - all BLE commands must end with EOT character.
    /// Per BLE protocol v2.0.0: BLE commands use \x04, serial commands use \n.
    /// </summary>
    public const char CommandTerminator = '\x04';

    /// <summary>
    /// Recommended delay between commands in milliseconds.
    /// Per spec: wait 100ms for reliable processing.
    /// </summary>
    public const int CommandDelayMs = 100;

    /// <summary>
    /// Battery voltage threshold for good status (Primary blue).
    /// </summary>
    public const double BatteryGoodThreshold = 3.6;

    /// <summary>
    /// Battery voltage threshold for medium status (Warning amber).
    /// </summary>
    public const double BatteryMediumThreshold = 3.3;

    /// <summary>
    /// Battery voltage threshold for low battery warning (DangerDark).
    /// Below this voltage, user should be warned to charge.
    /// </summary>
    public const double BatteryLowThreshold = 3.3;

    /// <summary>
    /// Default scan timeout in seconds.
    /// </summary>
    public const int DefaultScanTimeoutSeconds = 10;

    /// <summary>
    /// Connection timeout in seconds.
    /// </summary>
    public const int ConnectionTimeoutSeconds = 15;

    /// <summary>
    /// Timeout for PING command in milliseconds.
    /// </summary>
    public const int PingTimeoutMs = 2000;

    /// <summary>
    /// Session status polling interval in seconds.
    /// App should poll every 5-10 seconds during active sessions.
    /// </summary>
    public const int SessionStatusPollIntervalSeconds = 5;

    /// <summary>
    /// Connection health check interval in seconds.
    /// App should send PING every 30 seconds to detect disconnects.
    /// </summary>
    public const int ConnectionHealthCheckIntervalSeconds = 30;
}
