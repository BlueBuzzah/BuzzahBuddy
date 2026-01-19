namespace BuzzahBuddy.Models;

/// <summary>
/// Represents the current state of the main dashboard.
/// Used to show appropriate UI and call-to-action buttons.
/// </summary>
public enum DashboardState
{
    /// <summary>
    /// No device connected. Show "Connect Device" CTA.
    /// </summary>
    Disconnected,

    /// <summary>
    /// Actively connecting to a device. Show progress indicator.
    /// </summary>
    Connecting,

    /// <summary>
    /// Device connected but no active session. Show "Start Therapy" CTA.
    /// </summary>
    Idle,

    /// <summary>
    /// Therapy session is actively running. Show session progress and "Pause" CTA.
    /// </summary>
    SessionActive,

    /// <summary>
    /// Therapy session is paused. Show "Resume" and "Stop" CTAs.
    /// </summary>
    SessionPaused,

    /// <summary>
    /// Connection error occurred. Show "Reconnect" CTA.
    /// </summary>
    Error
}
