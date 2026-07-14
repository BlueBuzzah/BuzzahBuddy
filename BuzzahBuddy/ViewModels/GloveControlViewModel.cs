using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.AppLifecycle;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using static BuzzahBuddy.Services.Glove.ErrorMessageHelper;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the glove control page.
/// Handles therapy session control, profile selection, and battery monitoring.
/// </summary>
public partial class GloveControlViewModel : BaseViewModel
{
    private readonly IGloveControlService _gloveControlService;
    private readonly IBluetoothService _bluetoothService;
    private readonly IDataStorageService _storageService;
    private readonly IReconnectionService _reconnectionService;
    private TherapySession? _currentSession;
    private System.Timers.Timer? _statusPollTimer;
    private System.Timers.Timer? _healthCheckTimer;
    private readonly IAppLifecycleService _appLifecycle;
    private bool _pollingPausedByLifecycle;
    private bool _isBackgrounded;
    private SessionState _previousSessionState = SessionState.IDLE;
    private bool _userRequestedStop;
    private int _consecutivePollFailures;
    private int _consecutiveHealthCheckFailures;
    private const int PollFailureWarningThreshold = 2;
    private const int PollFailureReconnectThreshold = 3;
    private const string ConnectionUnstableWarning = "Connection unstable — trying to recover…";
    private const string ProfileRebootWarning = "Gloves are restarting to apply the new profile…";

    [ObservableProperty]
    private ObservableCollection<ProfileItemViewModel> _availableProfiles = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSelectedProfile))]
    private TherapyProfile? _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMoreButtonText))]
    private bool _isShowingAdvancedProfiles;

    /// <summary>
    /// Primary profiles shown by default (Noisy, Regular, Quick Test).
    /// </summary>
    public IEnumerable<ProfileItemViewModel> PrimaryProfiles =>
        AvailableProfiles.Where(p => p.IsPrimaryProfile).OrderBy(p => p.IsRecommended ? 0 : 1);

    /// <summary>
    /// Advanced profiles shown when "Show More" is expanded (Hybrid, Custom, Gentle).
    /// </summary>
    public IEnumerable<ProfileItemViewModel> AdvancedProfiles =>
        AvailableProfiles.Where(p => p.IsAdvancedProfile);

    /// <summary>
    /// Text for the Show More/Less toggle button.
    /// </summary>
    public string ShowMoreButtonText => IsShowingAdvancedProfiles ? "Show Less" : "Show More Profiles";

    [ObservableProperty]
    private SessionStatus _sessionStatus = SessionStatus.CreateIdle();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSessionInactive))]
    private bool _isSessionActive;

    [ObservableProperty]
    private bool _isSessionRunning;

    [ObservableProperty]
    private bool _isSessionPaused;

    [ObservableProperty]
    private double _batteryPrimaryVoltage;

    [ObservableProperty]
    private double _batterySecondaryVoltage;

    [ObservableProperty]
    private int _batteryPrimaryPercentage;

    [ObservableProperty]
    private int _batterySecondaryPercentage;

    [ObservableProperty]
    private Color _batteryPrimaryColor = Colors.Green;

    [ObservableProperty]
    private Color _batterySecondaryColor = Colors.Green;

    /// <summary>
    /// Accessibility description for the primary battery status.
    /// </summary>
    public string BatteryPrimaryDescription =>
        $"Primary battery: {BatteryPrimaryPercentage} percent, {BatteryHelper.GetBatteryStatusText(BatteryPrimaryVoltage)}";

    /// <summary>
    /// Accessibility description for the secondary battery status.
    /// </summary>
    public string BatterySecondaryDescription =>
        $"Secondary battery: {BatterySecondaryPercentage} percent, {BatteryHelper.GetBatteryStatusText(BatterySecondaryVoltage)}";

    [ObservableProperty]
    private bool _showBatteryRefresh = true;

    [ObservableProperty]
    private bool _isLoadingProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRefreshingBattery))]
    private bool _isRefreshingBattery;

    [ObservableProperty]
    private string? _batteryStatusMessage;

    [ObservableProperty]
    private string? _sessionWarningMessage;

    [ObservableProperty]
    private string? _profileLoadingMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotTestingConnection))]
    private bool _isTestingConnection;

    [ObservableProperty]
    private string _sessionButtonText = "Start Session";

    [ObservableProperty]
    private string _sessionButtonDescription = "Starts a new therapy session";

    [ObservableProperty]
    private bool _isConnectionHealthy = true;

    [ObservableProperty]
    private DateTime? _lastSuccessfulPing;

    /// <summary>
    /// Single source of truth for connection state. Exposed for XAML binding.
    /// </summary>
    public IConnectionStateService ConnectionInfo { get; }

    /// <summary>
    /// Whether to show the reconnection banner (reconnecting or has a message like "Connection lost").
    /// </summary>
    public bool ShowReconnectionBanner =>
        ConnectionInfo.IsReconnecting || !string.IsNullOrEmpty(ConnectionInfo.ReconnectionMessage);

    /// <summary>
    /// Inverse of IsTestingConnection for XAML MultiBinding (MAUI ignores Converter on child Binding in MultiBinding).
    /// </summary>
    public bool IsNotTestingConnection => !IsTestingConnection;

    /// <summary>
    /// Inverse of IsRefreshingBattery for XAML MultiBinding (MAUI ignores Converter on child Binding in MultiBinding).
    /// </summary>
    public bool IsNotRefreshingBattery => !IsRefreshingBattery;

    /// <summary>
    /// True when no session is active. For XAML MultiBinding use.
    /// </summary>
    public bool IsSessionInactive => !IsSessionActive;

    /// <summary>
    /// True when no therapy profile is selected. For XAML MultiBinding use.
    /// </summary>
    public bool HasNoSelectedProfile => SelectedProfile == null;

    public GloveControlViewModel(
        IGloveControlService gloveControlService,
        IBluetoothService bluetoothService,
        IDataStorageService storageService,
        IReconnectionService reconnectionService,
        IConnectionStateService connectionStateService,
        IAppLifecycleService appLifecycleService)
    {
        _gloveControlService = gloveControlService;
        _bluetoothService = bluetoothService;
        _storageService = storageService;
        _reconnectionService = reconnectionService;
        ConnectionInfo = connectionStateService;

        Title = "Therapy Control";

        // Subscribe to connection events
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        // Subscribe to centralized connection state changes
        ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;

        // Pause/resume BLE polling with the app window lifecycle
        _appLifecycle = appLifecycleService;
        _appLifecycle.Stopped += OnAppStopped;
        _appLifecycle.Resumed += OnAppResumed;

        // Initialize
        LoadProfilesAsync().SafeFireAndForget("[GLOVECONTROL]");
        UpdateConnectionState();
    }

    [RelayCommand]
    private async Task ToggleSessionAsync()
    {
        if (!ConnectionInfo.IsConnected)
        {
            await Shell.Current.DisplayAlert(
                "Not Connected",
                "Please connect to a BlueBuzzah glove first.",
                "OK");
            return;
        }

        if (!IsSessionActive && SelectedProfile == null)
        {
            await Shell.Current.DisplayAlert(
                "No Profile Selected",
                "Please select a therapy profile first.",
                "OK");
            return;
        }

        IsBusy = true;

        try
        {
            if (!IsSessionActive)
            {
                if (_gloveControlService.DeviceProfileId > 0 &&
                    SelectedProfile != null &&
                    SelectedProfile.ProfileId != _gloveControlService.DeviceProfileId)
                {
                    await Shell.Current.DisplayAlert(
                        "Profile Not Applied",
                        $"The gloves are still using a different profile. Tap \"{SelectedProfile.Name}\" again to apply it before starting.",
                        "OK");
                    return;
                }

                // Start session
                await _gloveControlService.StartSessionAsync();

                // Create new therapy session
                _currentSession = new TherapySession
                {
                    StartTime = DateTime.Now,
                    ProfileUsed = SelectedProfile,
                    ProfileId = SelectedProfile!.ProfileId,
                    DeviceId = _bluetoothService.ConnectedDevice?.Id,
                    Status = SessionState.RUNNING
                };

                StartStatusPolling();
                await UpdateSessionStatusAsync();
                SemanticScreenReader.Announce("Therapy session started");
            }
            else if (IsSessionRunning)
            {
                // Pause session
                await _gloveControlService.PauseSessionAsync();
                await UpdateSessionStatusAsync();
                SemanticScreenReader.Announce("Session paused");
            }
            else if (IsSessionPaused)
            {
                // Resume session
                await _gloveControlService.ResumeSessionAsync();
                await UpdateSessionStatusAsync();
                SemanticScreenReader.Announce("Session resumed");
            }
        }
        catch (BlueBuzzahCommandException ex)
        {
            var (title, message) = ErrorMessageHelper.GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        catch (Exception ex)
        {
            var title = ErrorMessageHelper.GetErrorTitle(ex);
            var message = ErrorMessageHelper.GetErrorMessage(ex);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        if (!IsSessionActive)
            return;

        // Check if session is less than 2 minutes and show confirmation
        var elapsed = SessionStatus.ElapsedTimeSeconds;
        if (elapsed < 120) // Less than 2 minutes
        {
            var minutes = (int)(elapsed / 60);
            var seconds = (int)(elapsed % 60);
            var timeString = minutes > 0
                ? $"{minutes} minute{(minutes > 1 ? "s" : "")} and {seconds} second{(seconds != 1 ? "s" : "")}"
                : $"{seconds} second{(seconds != 1 ? "s" : "")}";

            var confirm = await Shell.Current.DisplayAlert(
                "Stop Session?",
                $"You've only been in this session for {timeString}. Your progress will be saved. Do you want to stop?",
                "Keep Going",
                "Stop Session");

            if (confirm) // "Keep Going" was selected
                return;
        }

        IsBusy = true;

        try
        {
            // Stop session
            _userRequestedStop = true;
            await _gloveControlService.StopSessionAsync();

            if (_currentSession != null)
            {
                _currentSession.EndTime = DateTime.Now;
                _currentSession.IsCompleted = true;
                _currentSession.Status = SessionState.IDLE;
                await _storageService.SaveSessionAsync(_currentSession);
                _currentSession = null;
            }

            StopStatusPolling();
            await UpdateSessionStatusAsync();
            SemanticScreenReader.Announce("Session stopped");
        }
        catch (BlueBuzzahCommandException ex)
        {
            var (title, message) = ErrorMessageHelper.GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        catch (Exception ex)
        {
            var title = ErrorMessageHelper.GetErrorTitle(ex);
            var message = ErrorMessageHelper.GetErrorMessage(ex);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }


    [RelayCommand]
    private void ToggleAdvancedProfiles()
    {
        IsShowingAdvancedProfiles = !IsShowingAdvancedProfiles;
    }

    [RelayCommand]
    private async Task SelectProfileAsync(ProfileItemViewModel profileItem)
    {
        if (profileItem?.Profile == null)
            return;

        // Selecting the profile already on the device is just a UI highlight
        var isDeviceCurrent = profileItem.Profile.ProfileId == _gloveControlService.DeviceProfileId;

        if (!isDeviceCurrent && ConnectionInfo.IsConnected)
        {
            if (IsSessionActive)
            {
                await Shell.Current.DisplayAlert(
                    "Session Active",
                    "Stop the current session before changing profiles.",
                    "OK");
                return;
            }

            var confirm = await Shell.Current.DisplayAlert(
                "Change Profile?",
                $"Switching to \"{profileItem.Profile.Name}\" will restart your gloves. " +
                "They will reconnect automatically in a few moments.",
                "Change Profile",
                "Cancel");
            if (!confirm)
                return;

            IsBusy = true;
            try
            {
                await _gloveControlService.LoadProfileAsync(profileItem.Profile.ProfileId);
                SessionWarningMessage = ProfileRebootWarning;
            }
            catch (BlueBuzzahCommandException ex)
            {
                var (title, message) = ErrorMessageHelper.GetFriendlyError(ex.Message);
                await Shell.Current.DisplayAlert(title, message, "OK");
                return;
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert(ErrorMessageHelper.GetErrorTitle(ex), ErrorMessageHelper.GetErrorMessage(ex), "OK");
                return;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Deselect all profiles
        foreach (var item in AvailableProfiles)
        {
            item.IsSelected = false;
        }

        // Select the tapped profile
        profileItem.IsSelected = true;

        // Ensure PropertyChanged fires on UI thread for nested bindings
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SelectedProfile = profileItem.Profile;
        });
    }

    [RelayCommand]
    private async Task NavigateToDevicesAsync()
    {
        await Shell.Current.GoToAsync(Routes.Devices);
    }

    [RelayCommand]
    private async Task NavigateToProfileSettingsAsync()
    {
        await Shell.Current.GoToAsync(Routes.ProfileSettings);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!ConnectionInfo.IsConnected)
        {
            await Shell.Current.DisplayAlert(
                "Not Connected",
                "Please connect to a BlueBuzzah glove first.",
                "OK");
            return;
        }

        IsBusy = true;
        IsTestingConnection = true;

        try
        {
            var success = await _gloveControlService.PingAsync();

            if (success)
            {
                await Shell.Current.DisplayAlert(
                    "Test Successful",
                    "Connection test completed successfully!",
                    "OK");
            }
            else
            {
                await Shell.Current.DisplayAlert(
                    "Test Failed",
                    "Connection test failed. Please check your connection.",
                    "OK");
            }
        }
        finally
        {
            IsTestingConnection = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshBatteryAsync()
    {
        if (!ConnectionInfo.IsConnected)
        {
            await Shell.Current.DisplayAlert(
                "Not Connected",
                "Please connect to a BlueBuzzah glove first.",
                "OK");
            return;
        }

        IsBusy = true;
        IsRefreshingBattery = true;

        try
        {
            var (primaryVoltage, secondaryVoltage) = await _gloveControlService.GetBatteryAsync();

            // Debug: Log raw voltage values from device
            System.Diagnostics.Debug.WriteLine($"[BATTERY] Raw voltages - Primary: {primaryVoltage}V, Secondary: {secondaryVoltage}V");

            BatteryPrimaryVoltage = primaryVoltage;
            BatterySecondaryVoltage = secondaryVoltage;

            // Calculate percentages (3.0V = 0%, 4.2V = 100%)
            BatteryPrimaryPercentage = BatteryHelper.VoltageToPercentage(primaryVoltage);
            BatterySecondaryPercentage = BatteryHelper.VoltageToPercentage(secondaryVoltage);

            // Debug: Log calculated percentages
            System.Diagnostics.Debug.WriteLine($"[BATTERY] Percentages - Primary: {BatteryPrimaryPercentage}%, Secondary: {BatterySecondaryPercentage}%");

            // Update colors based on voltage thresholds
            BatteryPrimaryColor = BatteryHelper.GetBatteryColorFromVoltage(primaryVoltage);
            BatterySecondaryColor = BatteryHelper.GetBatteryColorFromVoltage(secondaryVoltage);

            // Notify accessibility description properties
            OnPropertyChanged(nameof(BatteryPrimaryDescription));
            OnPropertyChanged(nameof(BatterySecondaryDescription));

            // Progressive disclosure: Hide refresh button when battery is good
            var minPercentage = Math.Min(BatteryPrimaryPercentage, BatterySecondaryPercentage);
            ShowBatteryRefresh = minPercentage <= 50;

            // Check for low battery warnings
            await CheckBatteryWarningAsync();

            BatteryStatusMessage = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Battery refresh error: {ex.Message}");
            BatteryStatusMessage = "Battery status unavailable";
        }
        finally
        {
            IsRefreshingBattery = false;
            IsBusy = false;
        }
    }

    partial void OnSelectedProfileChanged(TherapyProfile? value)
    {
        if (value == null)
            return;

        // Cannot change profile during active session
        if (IsSessionActive)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Shell.Current.DisplayAlert(
                    "Session Active",
                    "Stop the current session to change profiles.",
                    "OK");
            });
            return;
        }

        // Profile selection only updates local state - device is updated when session starts
        System.Diagnostics.Debug.WriteLine($"[PROFILE] Selected profile {value.ProfileId}: {value.Name}");

        // Save selection preference for next app launch
        _ = _storageService.SaveLastProfileAsync(value.ProfileId);
    }

    private async Task LoadProfilesAsync()
    {
        System.Diagnostics.Debug.WriteLine("[PROFILES] LoadProfilesAsync starting...");

        try
        {
            // Load all 6 preset profiles (device connection not required for profile list)
            var profiles = TherapyProfile.GetPresetProfiles();
            System.Diagnostics.Debug.WriteLine($"[PROFILES] Loaded {profiles.Count} preset profiles");

            AvailableProfiles.Clear();
            foreach (var profile in profiles)
            {
                AvailableProfiles.Add(new ProfileItemViewModel(profile));
                System.Diagnostics.Debug.WriteLine($"[PROFILES]   - ID={profile.ProfileId}, Name={profile.Name}");
            }

            // Notify filtered profile properties
            OnPropertyChanged(nameof(PrimaryProfiles));
            OnPropertyChanged(nameof(AdvancedProfiles));

            // Load last used profile or default to Noisy (profile 2)
            var lastProfileId = await _storageService.GetLastProfileAsync();
            var selectedItem = AvailableProfiles.FirstOrDefault(p => p.ProfileId == lastProfileId)
                            ?? AvailableProfiles.FirstOrDefault(p => p.ProfileId == 2)
                            ?? AvailableProfiles.FirstOrDefault();

            if (selectedItem != null)
            {
                selectedItem.IsSelected = true;
                SelectedProfile = selectedItem.Profile;
                System.Diagnostics.Debug.WriteLine($"[PROFILES] Selected profile: {selectedItem.Name}");

                // If selected profile is advanced, show advanced section
                if (selectedItem.IsAdvancedProfile)
                {
                    IsShowingAdvancedProfiles = true;
                }
            }

            ProfileLoadingMessage = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PROFILES] Load profiles error: {ex.Message}");

            // Fallback to all preset profiles
            var presetProfiles = TherapyProfile.GetPresetProfiles();
            System.Diagnostics.Debug.WriteLine($"[PROFILES] Using fallback with {presetProfiles.Count} preset profiles");

            AvailableProfiles.Clear();
            foreach (var profile in presetProfiles)
            {
                AvailableProfiles.Add(new ProfileItemViewModel(profile));
            }

            OnPropertyChanged(nameof(PrimaryProfiles));
            OnPropertyChanged(nameof(AdvancedProfiles));

            var defaultItem = AvailableProfiles.FirstOrDefault(p => p.ProfileId == 2)
                           ?? AvailableProfiles.FirstOrDefault();
            if (defaultItem != null)
            {
                defaultItem.IsSelected = true;
                SelectedProfile = defaultItem.Profile;
                System.Diagnostics.Debug.WriteLine($"[PROFILES] Fallback selected: {defaultItem.Name}");
            }

            ProfileLoadingMessage = "Showing default profiles — device profiles could not be loaded.";
        }
    }

    private async Task UpdateSessionStatusAsync()
    {
        if (!ConnectionInfo.IsConnected)
        {
            SessionStatus = SessionStatus.CreateIdle();
            UpdateSessionState();
            return;
        }

        try
        {
            var previousProgress = SessionStatus.Progress;
            SessionStatus = await _gloveControlService.GetSessionStatusAsync();

            // Detect unexpected session end (e.g., secondary glove disconnected, error,
            // critical battery). State-aware: LOW_BATTERY keeps the session running and
            // surfaces a non-blocking warning instead of an "ended unexpectedly" alert.
            bool wasActive = _previousSessionState is SessionState.RUNNING or SessionState.PAUSED or SessionState.LOW_BATTERY or SessionState.STOPPING;
            string? endReason = SessionStatus.Status switch
            {
                SessionState.ERROR => "The gloves reported an error and stopped the session.",
                SessionState.CONNECTION_LOST => "The gloves lost connection and stopped the session.",
                SessionState.CRITICAL_BATTERY => "A glove battery is critically low. The session was stopped — please charge the gloves.",
                SessionState.IDLE when SessionStatus.Progress < 100 => "The therapy session stopped unexpectedly. The secondary glove may have disconnected.",
                _ => null
            };

            if (wasActive && endReason != null && !_userRequestedStop)
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Shell.Current.DisplayAlert(
                        "Session Ended Unexpectedly",
                        endReason,
                        "OK");
                });
            }

            var previousState = _previousSessionState;
            _previousSessionState = SessionStatus.Status;
            _userRequestedStop = false;

            // Update current session tracking
            if (_currentSession != null && SessionStatus.IsActive)
            {
                _currentSession.ElapsedTimeSeconds = SessionStatus.ElapsedTimeSeconds;
                _currentSession.Progress = SessionStatus.Progress;
                _currentSession.Status = SessionStatus.Status;
            }

            // Check for session completion (Progress is a 0-100 percent, not a 0-1 fraction)
            if (SessionStatus.Progress >= 100 && previousProgress < 100)
            {
                await HandleSessionCompletionAsync();
            }

            UpdateSessionState();

            _consecutivePollFailures = 0;
            if (SessionStatus.Status == SessionState.LOW_BATTERY)
            {
                const string lowBatteryMessage = "Glove battery is low — session will continue.";
                SessionWarningMessage = lowBatteryMessage;

                // Announce once on transition into LOW_BATTERY, not on every poll.
                if (previousState != SessionState.LOW_BATTERY)
                {
                    SemanticScreenReader.Announce(lowBatteryMessage);
                }
            }
            else
            {
                SessionWarningMessage = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Status update error: {ex.Message}");
            _consecutivePollFailures++;

            if (_consecutivePollFailures >= PollFailureReconnectThreshold)
            {
                SessionWarningMessage = null;
                IsConnectionHealthy = false;
            }
            else if (_consecutivePollFailures >= PollFailureWarningThreshold)
            {
                SessionWarningMessage = "Session data may be outdated";
            }
        }
    }

    private void UpdateSessionState()
    {
        IsSessionActive = SessionStatus.IsActive;
        IsSessionRunning = SessionStatus.IsRunning;
        IsSessionPaused = SessionStatus.IsPaused;

        // Update button text and description based on current state
        if (!IsSessionActive)
        {
            SessionButtonText = "Start Session";
            SessionButtonDescription = "Starts a new therapy session";
        }
        else if (IsSessionRunning)
        {
            SessionButtonText = "Pause Session";
            SessionButtonDescription = "Pauses the active therapy session";
        }
        else if (IsSessionPaused)
        {
            SessionButtonText = "Resume Session";
            SessionButtonDescription = "Resumes the paused therapy session";
        }
    }

    private void StartStatusPolling()
    {
        _statusPollTimer?.Stop();
        _statusPollTimer?.Dispose();
        _statusPollTimer = new System.Timers.Timer(BlueBuzzahConstants.SessionStatusPollIntervalSeconds * 1000);
        _statusPollTimer.Elapsed += async (s, e) =>
            await MainThread.InvokeOnMainThreadAsync(UpdateSessionStatusAsync);
        _statusPollTimer.Start();
    }

    private void StopStatusPolling()
    {
        _statusPollTimer?.Stop();
        _statusPollTimer?.Dispose();
        _statusPollTimer = null;
    }

    private void StartConnectionHealthCheck()
    {
        _consecutiveHealthCheckFailures = 0;
        _healthCheckTimer?.Stop();
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = new System.Timers.Timer(BlueBuzzahConstants.ConnectionHealthCheckIntervalSeconds * 1000);
        _healthCheckTimer.Elapsed += async (s, e) =>
            await MainThread.InvokeOnMainThreadAsync(CheckConnectionHealthAsync);
        _healthCheckTimer.Start();
    }

    private void StopConnectionHealthCheck()
    {
        _healthCheckTimer?.Stop();
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;
    }

    private async Task CheckConnectionHealthAsync()
    {
        if (!ConnectionInfo.IsConnected)
        {
            IsConnectionHealthy = false;
            return;
        }

        bool pingSuccess;
        try
        {
            pingSuccess = await _gloveControlService.PingAsync(timeoutMs: BlueBuzzahConstants.PingTimeoutMs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Connection health check error: {ex.Message}");
            pingSuccess = false;
        }

        if (pingSuccess)
        {
            IsConnectionHealthy = true;
            LastSuccessfulPing = DateTime.Now;
            _consecutiveHealthCheckFailures = 0;
            if (SessionWarningMessage == ConnectionUnstableWarning)
            {
                SessionWarningMessage = null;
            }
            return;
        }

        _consecutiveHealthCheckFailures++;
        IsConnectionHealthy = false;
        SessionWarningMessage = ConnectionUnstableWarning;

        // Two consecutive missed pings (60s of silence): force a reconnect cycle
        if (_consecutiveHealthCheckFailures >= 2)
        {
            _consecutiveHealthCheckFailures = 0;
            try
            {
                await _bluetoothService.DisconnectForReconnectAsync();
            }
            catch (Exception disconnectEx)
            {
                System.Diagnostics.Debug.WriteLine($"Disconnect for reconnect failed: {disconnectEx.Message}");
            }
        }
    }

    private void UpdateConnectionState()
    {
        if (ConnectionInfo.IsConnected)
        {
            RefreshBatteryAsync().SafeFireAndForget("[GLOVECONTROL]");
            SyncSelectedProfileFromDeviceAsync().SafeFireAndForget("[GLOVECONTROL]");
            StartConnectionHealthCheck();
        }
        else
        {
            StopConnectionHealthCheck();
            IsConnectionHealthy = false;
        }
    }

    /// <summary>
    /// Queries the device for its currently loaded profile and syncs local selection state
    /// to match. Called on every (re)connect so the UI never disagrees with the device.
    /// </summary>
    private async Task SyncSelectedProfileFromDeviceAsync()
    {
        try
        {
            await _gloveControlService.GetDeviceInfoAsync();

            // Clear a stuck "restarting" banner from a profile-change reboot on any
            // successful INFO fetch — but only that banner, so warnings owned by other
            // paths (low battery, unstable connection) aren't wiped here.
            if (SessionWarningMessage == ProfileRebootWarning)
            {
                SessionWarningMessage = null;
            }

            var deviceProfileId = _gloveControlService.DeviceProfileId;
            if (deviceProfileId <= 0)
                return;

            var match = AvailableProfiles.FirstOrDefault(p => p.Profile?.ProfileId == deviceProfileId);
            if (match != null)
            {
                foreach (var item in AvailableProfiles)
                {
                    item.IsSelected = item == match;
                }
                SelectedProfile = match.Profile;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GLOVECONTROL] Profile sync failed: {ex.Message}");
        }
    }

    private void OnAppStopped(object? sender, EventArgs e)
    {
        // Pause BLE polling while backgrounded; the OS may kill timers anyway.
        _isBackgrounded = true;
        _pollingPausedByLifecycle = _statusPollTimer != null;
        StopStatusPolling();
        StopConnectionHealthCheck();
    }

    private void OnAppResumed(object? sender, EventArgs e)
    {
        _isBackgrounded = false;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!ConnectionInfo.IsConnected)
                return;

            StartConnectionHealthCheck();
            if (_pollingPausedByLifecycle || IsSessionActive)
            {
                StartStatusPolling();
                _pollingPausedByLifecycle = false;
            }
            // Resync session state — it may have changed or ended while backgrounded.
            UpdateSessionStatusAsync().SafeFireAndForget("[GLOVECONTROL]");
        });
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Use state parameter directly — ConnectionInfo may not have updated yet
            var isConnected = state == ConnectionState.Connected;

            if (isConnected)
            {
                if (!_isBackgrounded)
                    StartConnectionHealthCheck();
                RefreshBatteryAsync().SafeFireAndForget("[GLOVECONTROL]");
                SyncSelectedProfileFromDeviceAsync().SafeFireAndForget("[GLOVECONTROL]");
            }
            else
            {
                StopConnectionHealthCheck();
                IsConnectionHealthy = false;
            }

            // Session teardown on disconnect
            if (!isConnected && IsSessionActive)
            {
                StopStatusPolling();
                SessionStatus = SessionStatus.CreateIdle();
                UpdateSessionState();

                // Persist incomplete session
                if (_currentSession != null)
                {
                    _currentSession.EndTime = DateTime.Now;
                    _currentSession.IsCompleted = false;
                    _storageService.SaveSessionAsync(_currentSession).SafeFireAndForget("[GLOVECONTROL]");
                    _currentSession = null;
                }
            }

            var announcement = state switch
            {
                ConnectionState.Connected => "Device connected",
                ConnectionState.Disconnected => "Device disconnected",
                ConnectionState.Error => "Device connection lost",
                ConnectionState.Reconnecting => "Attempting to reconnect",
                _ => null
            };
            if (announcement != null)
            {
                SemanticScreenReader.Announce(announcement);
            }
        });
    }

    private async Task HandleSessionCompletionAsync()
    {
        // Stop polling and cleanup
        StopStatusPolling();

        // Mark session as complete
        if (_currentSession != null)
        {
            _currentSession.EndTime = DateTime.Now;
            _currentSession.IsCompleted = true;
            _currentSession.Status = SessionState.IDLE;
        }

        // Run on UI thread
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            // Show completion dialog with rating option
            var ratingOptions = new[] { "Skip", "1 - Not Effective", "2", "3 - Somewhat Effective", "4", "5 - Very Effective" };
            var rating = await Shell.Current.DisplayActionSheet(
                "Session Complete!",
                null,
                null,
                ratingOptions);

            // Parse rating
            int? effectivenessRating = null;
            if (rating != null && rating != "Skip" && char.IsDigit(rating[0]))
            {
                effectivenessRating = int.Parse(rating[0].ToString());
            }

            // Ask for notes if they rated the session
            string? notes = null;
            if (effectivenessRating.HasValue)
            {
                notes = await Shell.Current.DisplayPromptAsync(
                    "Session Notes",
                    "Any notes about this session? (Optional)",
                    placeholder: "e.g., felt good, reduced tremors...",
                    maxLength: 500,
                    keyboard: Keyboard.Text);
            }

            // Save session with rating and notes
            if (_currentSession != null)
            {
                _currentSession.EffectivenessRating = effectivenessRating;
                _currentSession.Notes = notes;
                await _storageService.SaveSessionAsync(_currentSession);
            }

            // Show completion message
            await Shell.Current.DisplayAlert(
                "Therapy Complete",
                "Your therapy session has been saved. Great job!",
                "OK");

            // Reset session
            _currentSession = null;
            SessionStatus = SessionStatus.CreateIdle();
            UpdateSessionState();
        });
    }

    private async Task CheckBatteryWarningAsync()
    {
        if (BatteryPrimaryVoltage > 0 && BatteryPrimaryVoltage < BlueBuzzahConstants.BatteryLowThreshold)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.DisplayAlert(
                    "Low Battery Warning",
                    $"Primary device battery is low ({BatteryPrimaryVoltage:F2}V). Consider charging before starting a session.",
                    "OK");
            });
        }

        if (BatterySecondaryVoltage > 0 && BatterySecondaryVoltage < BlueBuzzahConstants.BatteryLowThreshold)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.DisplayAlert(
                    "Low Battery Warning",
                    $"Second glove battery is low ({BatterySecondaryVoltage:F2}V). Consider charging before starting a session.",
                    "OK");
            });
        }
    }

    private void OnConnectionInfoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IConnectionStateService.IsReconnecting) or nameof(IConnectionStateService.ReconnectionMessage))
        {
            OnPropertyChanged(nameof(ShowReconnectionBanner));
        }

        // GloveControl-specific: manage polling during reconnection
        if (e.PropertyName == nameof(IConnectionStateService.IsReconnecting))
        {
            if (ConnectionInfo.IsReconnecting)
            {
                StopStatusPolling();
            }
            else if (!ConnectionInfo.IsReconnecting && IsSessionActive)
            {
                // Reconnection ended (succeeded or failed) — restart polling if session active.
                // While backgrounded, defer to OnAppResumed instead of restarting a background timer.
                if (_isBackgrounded)
                    _pollingPausedByLifecycle = true;
                else
                    StartStatusPolling();
            }
        }
    }

    /// <summary>
    /// Unsubscribes from Bluetooth service events and stops timers to prevent memory leaks.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
            ConnectionInfo.PropertyChanged -= OnConnectionInfoPropertyChanged;
            _appLifecycle.Stopped -= OnAppStopped;
            _appLifecycle.Resumed -= OnAppResumed;
            StopStatusPolling();
            StopConnectionHealthCheck();
            System.Diagnostics.Debug.WriteLine("[GLOVECONTROL] ViewModel disposed, unsubscribed from events");
        }
        base.Dispose(disposing);
    }
}
