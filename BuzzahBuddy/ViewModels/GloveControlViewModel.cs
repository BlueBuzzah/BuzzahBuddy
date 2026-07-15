using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.AppLifecycle;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Preset profiles, used to map the device's profile id to a display profile.</summary>
    private readonly List<TherapyProfile> _profiles = TherapyProfile.GetPresetProfiles();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProfileName))]
    [NotifyPropertyChangedFor(nameof(ProfileSummary))]
    private TherapyProfile? _selectedProfile;

    /// <summary>
    /// Display name of the current therapy profile, or a prompt when none is selected.
    /// </summary>
    public string SelectedProfileName => SelectedProfile?.Name ?? "No profile selected";

    /// <summary>
    /// One-line summary of the current profile, e.g. "120 min session • 250 Hz".
    /// </summary>
    public string ProfileSummary => SelectedProfile is { } p
        ? $"{p.TimeSession} min session • {p.ActuatorFrequency} Hz"
        : string.Empty;

    [ObservableProperty]
    private SessionStatus _sessionStatus = SessionStatus.CreateIdle();

    [ObservableProperty]
    private bool _isSessionActive;

    [ObservableProperty]
    private bool _isSessionRunning;

    [ObservableProperty]
    private bool _isSessionPaused;

    // Null voltage = no reading available (missing key or firmware 0.00 sentinel)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryPrimaryText))]
    [NotifyPropertyChangedFor(nameof(BatteryPrimaryDescription))]
    private double? _batteryPrimaryVoltage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatterySecondaryText))]
    [NotifyPropertyChangedFor(nameof(BatterySecondaryDescription))]
    private double? _batterySecondaryVoltage;

    // Gray = no reading yet; real thresholds apply after the first battery poll
    [ObservableProperty]
    private Color _batteryPrimaryColor = Colors.Gray;

    [ObservableProperty]
    private Color _batterySecondaryColor = Colors.Gray;

    /// <summary>
    /// Display text for the primary battery, e.g. "60% (3.72V)" or "—".
    /// </summary>
    public string BatteryPrimaryText => BatteryReading.Format(BatteryPrimaryVoltage);

    /// <summary>
    /// Display text for the secondary battery, e.g. "58% (3.68V)" or "—".
    /// </summary>
    public string BatterySecondaryText => BatteryReading.Format(BatterySecondaryVoltage);

    /// <summary>
    /// Accessibility description for the primary battery status.
    /// </summary>
    public string BatteryPrimaryDescription =>
        BatteryPrimaryVoltage is { } v
            ? $"Primary battery: {BatteryReading.ToPercentage(v)} percent, {BatteryReading.GetBatteryStatusText(v)}"
            : "Primary battery: status unavailable";

    /// <summary>
    /// Accessibility description for the secondary battery status.
    /// </summary>
    public string BatterySecondaryDescription =>
        BatterySecondaryVoltage is { } v
            ? $"Secondary battery: {BatteryReading.ToPercentage(v)} percent, {BatteryReading.GetBatteryStatusText(v)}"
            : "Secondary battery: status unavailable";

    [ObservableProperty]
    private string? _sessionWarningMessage;

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

        Title = "Control";

        // Subscribe to connection events
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        // The service fetches INFO once per (re)connect; mirror its profile here
        _gloveControlService.DeviceProfileChanged += OnDeviceProfileChanged;

        // Session state from the service (fires on command success and every status
        // poll) — the UI must not depend on the first SESSION_STATUS poll succeeding
        _gloveControlService.SessionStateChanged += OnServiceSessionStateChanged;

        // Subscribe to centralized connection state changes
        ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;

        // Pause/resume BLE polling with the app window lifecycle
        _appLifecycle = appLifecycleService;
        _appLifecycle.Stopped += OnAppStopped;
        _appLifecycle.Resumed += OnAppResumed;

        // Initialize
        LoadSelectedProfileAsync().SafeFireAndForget("[GLOVECONTROL]");
        UpdateConnectionState();
    }

    /// <summary>
    /// Called from GloveControlPage.OnAppearing. Re-reads the last selected profile so a
    /// change made on the Device page (while disconnected) is reflected here.
    /// </summary>
    public void OnPageAppearing()
    {
        if (!IsSessionActive)
        {
            LoadSelectedProfileAsync().SafeFireAndForget("[GLOVECONTROL]");
        }

        // The connect happens on the Devices page; refresh here so this page always
        // shows battery values when visited, even if the on-connect refresh raced
        // the connection-state update. No low-battery alert on mere tab visits.
        if (ConnectionInfo.IsConnected)
        {
            RefreshBatteryAsync(warnIfLow: false).SafeFireAndForget("[GLOVECONTROL]");
        }
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
                        $"The gloves are still using a different profile. Go to the Devices page and apply \"{SelectedProfile.Name}\" before starting.",
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
    private async Task NavigateToDevicesAsync()
    {
        await Shell.Current.GoToAsync(Routes.Devices);
    }

    private async Task RefreshBatteryAsync(bool warnIfLow = true)
    {
        if (!ConnectionInfo.IsConnected)
            return;

        try
        {
            var (primaryVoltage, secondaryVoltage) = await _gloveControlService.GetBatteryAsync();

            BatteryPrimaryVoltage = primaryVoltage;
            BatterySecondaryVoltage = secondaryVoltage;

            // Gray = no reading; colored = real voltage thresholds
            BatteryPrimaryColor = primaryVoltage is { } pv
                ? BatteryReading.GetBatteryColorFromVoltage(pv) : Colors.Gray;
            BatterySecondaryColor = secondaryVoltage is { } sv
                ? BatteryReading.GetBatteryColorFromVoltage(sv) : Colors.Gray;

            // Check for low battery warnings (on connect, not on every tab visit)
            if (warnIfLow)
            {
                await CheckBatteryWarningAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Battery refresh error: {ex.Message}");
        }
    }

    private async Task LoadSelectedProfileAsync()
    {
        try
        {
            // Prefer the profile actually on the device, but only while connected —
            // DeviceProfileId is never reset on disconnect, so when disconnected it's a
            // stale snapshot and the saved selection (Device page picks) must win.
            var profileId = ConnectionInfo.IsConnected && _gloveControlService.DeviceProfileId > 0
                ? _gloveControlService.DeviceProfileId
                : await _storageService.GetLastProfileAsync();

            SelectedProfile = _profiles.FirstOrDefault(p => p.ProfileId == profileId)
                           ?? _profiles.FirstOrDefault(p => p.ProfileId == 2)
                           ?? _profiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PROFILES] Load selected profile error: {ex.Message}");
            SelectedProfile = _profiles.FirstOrDefault(p => p.ProfileId == 2) ?? _profiles.FirstOrDefault();
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
            StartConnectionHealthCheck();
        }
        else
        {
            StopConnectionHealthCheck();
            IsConnectionHealthy = false;
        }
    }

    /// <summary>
    /// Keeps the displayed profile in lockstep with the device. GloveControlService
    /// fetches INFO once per (re)connect and raises this for every INFO response.
    /// </summary>
    /// <summary>
    /// Mirrors the service's session state. StartSessionAsync/PauseSessionAsync/etc.
    /// synthesize a status on command success, so the session UI (progress card,
    /// button text, Stop button) updates even if the follow-up SESSION_STATUS poll
    /// fails or is slow while the gloves establish sync.
    /// </summary>
    private void OnServiceSessionStateChanged(object? sender, SessionStatus status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // The poll (UpdateSessionStatusAsync) owns completion/"ended unexpectedly"
            // detection and calls GetSessionStatusAsync, which fires this event with
            // the very status it then assigns to SessionStatus. Ignore that echo — by
            // the time this queued callback runs the poll has already assigned it — so
            // only out-of-band updates from Start/Pause/Resume/Stop flip the UI here,
            // and the poll's edge detection is never bypassed or double-run.
            if (ReferenceEquals(status, SessionStatus))
                return;

            SessionStatus = status;
            UpdateSessionState();
        });
    }

    private void OnDeviceProfileChanged(object? sender, int profileId)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var match = _profiles.FirstOrDefault(p => p.ProfileId == profileId);
            if (match != null)
            {
                SelectedProfile = match;
            }
        });
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
        if (BatteryPrimaryVoltage is { } primary && primary < BlueBuzzahConstants.BatteryLowThreshold)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.DisplayAlert(
                    "Low Battery Warning",
                    $"Primary device battery is low ({primary:F2}V). Consider charging before starting a session.",
                    "OK");
            });
        }

        if (BatterySecondaryVoltage is { } secondary && secondary < BlueBuzzahConstants.BatteryLowThreshold)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.DisplayAlert(
                    "Low Battery Warning",
                    $"Second glove battery is low ({secondary:F2}V). Consider charging before starting a session.",
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
            _gloveControlService.DeviceProfileChanged -= OnDeviceProfileChanged;
            _gloveControlService.SessionStateChanged -= OnServiceSessionStateChanged;
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
