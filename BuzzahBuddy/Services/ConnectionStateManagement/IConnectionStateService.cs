using System.ComponentModel;

namespace BuzzahBuddy.Services.ConnectionStateManagement;

/// <summary>
/// Single source of truth for device connection state.
/// ViewModels expose this service for XAML binding instead of independently tracking state.
/// </summary>
public interface IConnectionStateService : INotifyPropertyChanged
{
    /// <summary>Current connection state enum value.</summary>
    Models.ConnectionState ConnectionState { get; }

    /// <summary>True when ConnectionState == Connected.</summary>
    bool IsConnected { get; }

    /// <summary>Name of the connected device, or null when disconnected.</summary>
    string? ConnectedDeviceName { get; }

    /// <summary>True when auto-reconnection is in progress.</summary>
    bool IsReconnecting { get; }

    /// <summary>Human-readable reconnection status message.</summary>
    string? ReconnectionMessage { get; }
}
