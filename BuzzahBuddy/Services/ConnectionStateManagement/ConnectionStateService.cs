using System.ComponentModel;
using BuzzahBuddy.Services.Bluetooth;

namespace BuzzahBuddy.Services.ConnectionStateManagement;

/// <summary>
/// Singleton service that subscribes to IBluetoothService and IReconnectionService events
/// and maintains the canonical connection state. All property changes are marshalled
/// to the main thread for safe XAML binding.
/// </summary>
public class ConnectionStateService : IConnectionStateService, IDisposable
{
    private readonly IBluetoothService _bluetoothService;
    private readonly IReconnectionService _reconnectionService;
    private bool _disposed;

    private Models.ConnectionState _connectionState;
    private bool _isConnected;
    private string? _connectedDeviceName;
    private bool _isReconnecting;
    private string? _reconnectionMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Models.ConnectionState ConnectionState
    {
        get => _connectionState;
        private set
        {
            if (_connectionState != value)
            {
                _connectionState = value;
                OnPropertyChanged(nameof(ConnectionState));
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
            }
        }
    }

    public string? ConnectedDeviceName
    {
        get => _connectedDeviceName;
        private set
        {
            if (_connectedDeviceName != value)
            {
                _connectedDeviceName = value;
                OnPropertyChanged(nameof(ConnectedDeviceName));
            }
        }
    }

    public bool IsReconnecting
    {
        get => _isReconnecting;
        private set
        {
            if (_isReconnecting != value)
            {
                _isReconnecting = value;
                OnPropertyChanged(nameof(IsReconnecting));
            }
        }
    }

    public string? ReconnectionMessage
    {
        get => _reconnectionMessage;
        private set
        {
            if (_reconnectionMessage != value)
            {
                _reconnectionMessage = value;
                OnPropertyChanged(nameof(ReconnectionMessage));
            }
        }
    }

    public ConnectionStateService(
        IBluetoothService bluetoothService,
        IReconnectionService reconnectionService)
    {
        _bluetoothService = bluetoothService;
        _reconnectionService = reconnectionService;

        // Initialize backing fields synchronously so state is correct immediately
        // after construction — before any ViewModel reads ConnectionInfo.IsConnected.
        // PropertyChanged is NOT fired here (no UI subscribers exist yet during DI resolution).
        _connectionState = _bluetoothService.CurrentConnectionState;
        _isConnected = _connectionState == Models.ConnectionState.Connected;
        _connectedDeviceName = _bluetoothService.ConnectedDevice?.Name;

        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;
        _reconnectionService.ReconnectionStateChanged += OnReconnectionStateChanged;
    }

    private void OnConnectionStateChanged(object? sender, Models.ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectionState = state;
            IsConnected = state == Models.ConnectionState.Connected;
            ConnectedDeviceName = _bluetoothService.ConnectedDevice?.Name;
        });
    }

    private void OnReconnectionStateChanged(object? sender, ReconnectionStateEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsReconnecting = e.State == ReconnectionState.Reconnecting;
            ReconnectionMessage = e.State switch
            {
                ReconnectionState.Reconnecting => $"Reconnecting... (attempt {e.Attempt}/{e.MaxAttempts})",
                ReconnectionState.Succeeded => "Reconnected to BlueBuzzah gloves",
                ReconnectionState.Failed => "Could not reconnect. Please reconnect manually.",
                _ => null
            };
        });
    }

    private void OnPropertyChanged(string propertyName)
    {
        var handler = PropertyChanged;
        handler?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
            _reconnectionService.ReconnectionStateChanged -= OnReconnectionStateChanged;
            _disposed = true;
        }
    }
}
