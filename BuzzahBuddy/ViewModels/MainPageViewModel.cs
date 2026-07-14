using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.AppLifecycle;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Glove;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the main/home page smart dashboard.
/// Displays state-aware content with context-sensitive CTAs.
/// </summary>
public partial class MainPageViewModel : BaseViewModel
{
    private readonly IBluetoothService _bluetoothService;
    private readonly IGloveControlService _gloveControlService;
    private readonly IReconnectionService _reconnectionService;

    /// <summary>
    /// Centralized connection state service exposed for XAML binding.
    /// </summary>
    public IConnectionStateService ConnectionInfo { get; }

    #region Observable Properties

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryCTAText))]
    [NotifyPropertyChangedFor(nameof(PrimaryCTADescription))]
    [NotifyPropertyChangedFor(nameof(IsPrimaryCTAEnabled))]
    [NotifyPropertyChangedFor(nameof(IsConnecting))]
    [NotifyPropertyChangedFor(nameof(SecondaryCTAText))]
    [NotifyPropertyChangedFor(nameof(HasSecondaryCTA))]
    [NotifyPropertyChangedFor(nameof(SecondaryCTABackgroundColor))]
    [NotifyPropertyChangedFor(nameof(SecondaryCTATextColor))]
    [NotifyPropertyChangedFor(nameof(ShowSessionProgress))]
    [NotifyPropertyChangedFor(nameof(ShowBatteryStatus))]
    [NotifyPropertyChangedFor(nameof(ShowSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(StatusMessage))]
    private DashboardState _dashboardState = DashboardState.Disconnected;

    // Battery status (simplified - percentage only)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryPrimaryColor))]
    [NotifyPropertyChangedFor(nameof(BatteryStatusDescription))]
    private int _batteryPrimaryPercentage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatterySecondaryColor))]
    [NotifyPropertyChangedFor(nameof(BatteryStatusDescription))]
    private int _batterySecondaryPercentage;

    // Session status
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingTimeText))]
    private SessionStatus _sessionStatus = SessionStatus.CreateIdle();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSelectedProfile))]
    private string? _selectedProfileName;

    #endregion

    #region Computed Display Properties

    /// <summary>
    /// Text for the primary call-to-action button.
    /// </summary>
    public string PrimaryCTAText => DashboardState switch
    {
        DashboardState.Disconnected => "Connect Device",
        DashboardState.Connecting => "Connecting...",
        DashboardState.Idle => "Start Therapy",
        DashboardState.SessionActive => "Pause Session",
        DashboardState.SessionPaused => "Resume Session",
        DashboardState.Error => "Reconnect",
        _ => "Connect Device"
    };

    /// <summary>
    /// Accessibility description for the primary CTA.
    /// </summary>
    public string PrimaryCTADescription => DashboardState switch
    {
        DashboardState.Disconnected => "Navigate to device list to connect your BlueBuzzah gloves",
        DashboardState.Connecting => "Currently connecting to device",
        DashboardState.Idle => "Start a therapy session with the selected profile",
        DashboardState.SessionActive => "Pause the current therapy session",
        DashboardState.SessionPaused => "Resume the paused therapy session",
        DashboardState.Error => "Navigate to device list to reconnect",
        _ => "Connect your BlueBuzzah gloves"
    };

    /// <summary>
    /// Whether the primary CTA button is enabled.
    /// </summary>
    public bool IsPrimaryCTAEnabled => DashboardState != DashboardState.Connecting;

    /// <summary>
    /// Whether we're currently connecting to a device.
    /// </summary>
    public bool IsConnecting => DashboardState == DashboardState.Connecting;

    /// <summary>
    /// Whether to show the reconnection banner (reconnecting or has a failure message).
    /// </summary>
    public bool ShowReconnectionBanner =>
        ConnectionInfo.IsReconnecting || !string.IsNullOrEmpty(ConnectionInfo.ReconnectionMessage);

    /// <summary>
    /// Text for the secondary action link (optional).
    /// </summary>
    public string SecondaryCTAText => DashboardState switch
    {
        DashboardState.Idle => "Change Profile",
        DashboardState.SessionActive or DashboardState.SessionPaused => "Stop Session",
        _ => string.Empty
    };

    /// <summary>
    /// Whether to show the secondary CTA.
    /// </summary>
    public bool HasSecondaryCTA => !string.IsNullOrEmpty(SecondaryCTAText);

    /// <summary>
    /// Background color for secondary CTA (danger for Stop, neutral for Change Profile).
    /// Dark-mode-only colors matching BlueBuzzah.com design.
    /// </summary>
    public Color SecondaryCTABackgroundColor => DashboardState == DashboardState.Idle
        ? Color.FromArgb("#0d3a4d")   // CardBackgroundLight - teal accent
        : Color.FromArgb("#7f1d1d");  // Dark red for Stop action

    /// <summary>
    /// Text color for secondary CTA.
    /// </summary>
    public Color SecondaryCTATextColor => Color.FromArgb("#fafafa"); // TextPrimary

    /// <summary>
    /// Whether to show session progress (during active/paused session).
    /// </summary>
    public bool ShowSessionProgress => DashboardState is DashboardState.SessionActive or DashboardState.SessionPaused;

    /// <summary>
    /// Whether to show battery status (when connected).
    /// </summary>
    public bool ShowBatteryStatus => DashboardState is DashboardState.Idle or DashboardState.SessionActive or DashboardState.SessionPaused;

    /// <summary>
    /// Whether to show the selected profile (when idle).
    /// </summary>
    public bool ShowSelectedProfile => DashboardState == DashboardState.Idle && !string.IsNullOrEmpty(SelectedProfileName);

    /// <summary>
    /// Status message based on current dashboard state.
    /// </summary>
    public string StatusMessage => DashboardState switch
    {
        DashboardState.Disconnected => "Welcome! Connect your BlueBuzzah gloves to get started.",
        DashboardState.Connecting => "Connecting to your gloves...",
        DashboardState.Idle => $"Ready for therapy",
        DashboardState.SessionActive => "Session in progress",
        DashboardState.SessionPaused => "Session paused",
        DashboardState.Error => "Connection lost",
        _ => string.Empty
    };

    /// <summary>
    /// Formatted remaining time for display.
    /// </summary>
    public string RemainingTimeText => $"{SessionStatus.RemainingTimeFormatted} remaining";

    /// <summary>
    /// Color for primary battery indicator.
    /// </summary>
    public Color BatteryPrimaryColor => BatteryHelper.GetBatteryColor(BatteryPrimaryPercentage);

    /// <summary>
    /// Color for secondary battery indicator.
    /// </summary>
    public Color BatterySecondaryColor => BatteryHelper.GetBatteryColor(BatterySecondaryPercentage);

    /// <summary>
    /// Accessibility description for battery status.
    /// </summary>
    public string BatteryStatusDescription =>
        $"Primary battery {BatteryPrimaryPercentage} percent, Secondary battery {BatterySecondaryPercentage} percent";

    #endregion

    private readonly IAppLifecycleService _appLifecycle;

    public MainPageViewModel(
        IBluetoothService bluetoothService,
        IGloveControlService gloveControlService,
        IReconnectionService reconnectionService,
        IConnectionStateService connectionStateService,
        IAppLifecycleService appLifecycleService)
    {
        _bluetoothService = bluetoothService;
        _gloveControlService = gloveControlService;
        _reconnectionService = reconnectionService;
        ConnectionInfo = connectionStateService;

        Title = "BuzzahBuddy";

        // Subscribe to connection state changes
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;
        _gloveControlService.SessionStateChanged += OnSessionStateChanged;
        ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;

        // Refresh the dashboard when the app returns to the foreground
        _appLifecycle = appLifecycleService;
        _appLifecycle.Resumed += OnAppResumed;

        System.Diagnostics.Debug.WriteLine("[MAINPAGE] ViewModel created, subscribed to events");

        // Initialize state
        UpdateDashboardStateAsync().SafeFireAndForget("[MAINPAGE]");
    }

    #region Commands

    [RelayCommand]
    private async Task ExecutePrimaryCTAAsync()
    {
        switch (DashboardState)
        {
            case DashboardState.Disconnected:
            case DashboardState.Error:
                await Shell.Current.GoToAsync(Routes.Devices);
                break;

            case DashboardState.Idle:
                // Navigate to control page to start session
                await Shell.Current.GoToAsync(Routes.Control);
                break;

            case DashboardState.SessionActive:
                await PauseSessionAsync();
                break;

            case DashboardState.SessionPaused:
                await ResumeSessionAsync();
                break;
        }
    }

    [RelayCommand]
    private async Task ExecuteSecondaryCTAAsync()
    {
        switch (DashboardState)
        {
            case DashboardState.Idle:
                // Navigate to control page to change profile
                await Shell.Current.GoToAsync(Routes.Control);
                break;

            case DashboardState.SessionActive:
            case DashboardState.SessionPaused:
                await StopSessionAsync();
                break;
        }
    }

    [RelayCommand]
    private async Task NavigateToDevicesAsync()
    {
        await Shell.Current.GoToAsync(Routes.Devices);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;

        try
        {
            await UpdateDashboardStateAsync();

            if (ConnectionInfo.IsConnected)
            {
                await RefreshBatteryAsync();
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    #endregion

    #region Session Control

    private async Task PauseSessionAsync()
    {
        try
        {
            IsBusy = true;
            await _gloveControlService.PauseSessionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MAINPAGE] Pause error: {ex.Message}");
            await Shell.Current.DisplayAlert("Error", "Failed to pause session.", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ResumeSessionAsync()
    {
        try
        {
            IsBusy = true;
            await _gloveControlService.ResumeSessionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MAINPAGE] Resume error: {ex.Message}");
            await Shell.Current.DisplayAlert("Error", "Failed to resume session.", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopSessionAsync()
    {
        var confirm = await Shell.Current.DisplayAlert(
            "Stop Session",
            "Are you sure you want to stop the therapy session?",
            "Yes, Stop",
            "Cancel");

        if (!confirm) return;

        try
        {
            IsBusy = true;
            await _gloveControlService.StopSessionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MAINPAGE] Stop error: {ex.Message}");
            await Shell.Current.DisplayAlert("Error", "Failed to stop session.", "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region State Management

    /// <summary>
    /// Refreshes the dashboard state from services.
    /// Called when the page appears.
    /// </summary>
    public void RefreshConnectionState()
    {
        System.Diagnostics.Debug.WriteLine("[MAINPAGE] RefreshConnectionState called (OnAppearing)");
        UpdateDashboardStateAsync().SafeFireAndForget("[MAINPAGE]");
    }

    private async Task UpdateDashboardStateAsync()
    {
        if (ConnectionInfo.IsConnected)
        {
            // Get current profile name
            var profile = _gloveControlService.CurrentProfile;
            SelectedProfileName = profile?.Name ?? "Noisy (Recommended)";

            // Get current session status
            SessionStatus = _gloveControlService.CurrentSessionStatus;

            // Determine dashboard state based on session
            DashboardState = SessionStatus.Status switch
            {
                SessionState.RUNNING => DashboardState.SessionActive,
                SessionState.LOW_BATTERY => DashboardState.SessionActive,
                SessionState.PAUSED => DashboardState.SessionPaused,
                _ => DashboardState.Idle
            };

            // Refresh battery in background
            RefreshBatteryAsync().SafeFireAndForget("[MAINPAGE]");
        }
        else
        {
            SelectedProfileName = null;
            SessionStatus = SessionStatus.CreateIdle();
            BatteryPrimaryPercentage = 0;
            BatterySecondaryPercentage = 0;

            DashboardState = ConnectionInfo.ConnectionState switch
            {
                ConnectionState.Connecting => DashboardState.Connecting,
                ConnectionState.Error => DashboardState.Error,
                _ => DashboardState.Disconnected
            };
        }

    }

    private async Task RefreshBatteryAsync()
    {
        try
        {
            var (primaryVoltage, secondaryVoltage) = await _gloveControlService.GetBatteryAsync();
            BatteryPrimaryPercentage = BatteryHelper.VoltageToPercentage(primaryVoltage);
            BatterySecondaryPercentage = BatteryHelper.VoltageToPercentage(secondaryVoltage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MAINPAGE] Battery fetch error: {ex.Message}");
        }
    }
    #endregion

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        System.Diagnostics.Debug.WriteLine($"[MAINPAGE] ConnectionStateChanged: {state}");

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await UpdateDashboardStateAsync();
            SemanticScreenReader.Announce(StatusMessage);
        });
    }

    private void OnConnectionInfoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IConnectionStateService.IsReconnecting) or nameof(IConnectionStateService.ReconnectionMessage))
        {
            OnPropertyChanged(nameof(ShowReconnectionBanner));

            // Screen reader announcements for reconnection state changes
            if (e.PropertyName == nameof(IConnectionStateService.IsReconnecting) && !ConnectionInfo.IsReconnecting
                && ConnectionInfo.ConnectionState == ConnectionState.Connected)
            {
                SemanticScreenReader.Announce("Reconnected to BlueBuzzah gloves");
            }
            else if (e.PropertyName == nameof(IConnectionStateService.ReconnectionMessage)
                     && !string.IsNullOrEmpty(ConnectionInfo.ReconnectionMessage))
            {
                SemanticScreenReader.Announce(ConnectionInfo.ReconnectionMessage);
            }
        }

        if (e.PropertyName is nameof(IConnectionStateService.ConnectionState) or nameof(IConnectionStateService.IsConnected))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await UpdateDashboardStateAsync();
            });
        }
    }

    private void OnSessionStateChanged(object? sender, SessionStatus status)
    {
        System.Diagnostics.Debug.WriteLine($"[MAINPAGE] SessionStateChanged: {status.Status}");

        MainThread.BeginInvokeOnMainThread(() =>
        {
            SessionStatus = status;

            // Update dashboard state based on session
            if (ConnectionInfo.IsConnected)
            {
                DashboardState = status.Status switch
                {
                    SessionState.RUNNING => DashboardState.SessionActive,
                    SessionState.LOW_BATTERY => DashboardState.SessionActive,
                    SessionState.PAUSED => DashboardState.SessionPaused,
                    _ => DashboardState.Idle
                };
            }

            var announcement = status.Status switch
            {
                SessionState.RUNNING => "Therapy session started",
                SessionState.PAUSED => "Therapy session paused",
                SessionState.IDLE => "Therapy session ended",
                _ => null
            };
            if (announcement != null)
                SemanticScreenReader.Announce(announcement);
        });
    }

    #endregion

    #region Cleanup

    /// <summary>
    /// Unsubscribes from service events to prevent memory leaks.
    /// </summary>
    private void OnAppResumed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => RefreshConnectionState());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
            _gloveControlService.SessionStateChanged -= OnSessionStateChanged;
            ConnectionInfo.PropertyChanged -= OnConnectionInfoPropertyChanged;
            _appLifecycle.Resumed -= OnAppResumed;
            System.Diagnostics.Debug.WriteLine("[MAINPAGE] ViewModel disposed, unsubscribed from events");
        }
        base.Dispose(disposing);
    }

    #endregion
}
