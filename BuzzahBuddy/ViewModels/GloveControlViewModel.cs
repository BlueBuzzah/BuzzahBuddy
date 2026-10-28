using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.AppLifecycle;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Dialogs;
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
    private readonly IConfirmationService _confirmation;
    // One guard for Start, Pause/Resume and Stop: they replace each other in the same
    // spot, so a tremor's second tap must not land on the next control (Review focus 1).
    private readonly RepeatGuard _sessionControls = new(RepeatGuard.SessionControlWindow);
    private readonly AnnouncementThrottle _ringSpeech = new(TimeSpan.FromMinutes(1));
    private bool _showComplete;
    private int _completedTotalSeconds;
    private string? _sessionEndedMessage;
    private bool _sessionEndedByConnection;
    private bool _wasReconnecting;
    private (int Attempt, int Max) _reconnectAttempt;
    private TherapySession? _currentSession;
    // Set when the link dropped mid-session and an auto-reconnect is running: the
    // gloves keep the session going without the phone, so _currentSession is kept
    // until the reconnect reports back (see ResolveSessionAfterReconnect).
    private DateTime? _sessionDisconnectedAt;
    private CancellationTokenSource? _statusPollCts;
    private System.Timers.Timer? _healthCheckTimer;
    private readonly SessionClock _sessionClock = new();
    private IDispatcherTimer? _displayTickTimer;
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

    // Battery arrives with every INFO reply, including the service's post-connect
    // one; a tab visit only asks for a new INFO when the service's is this stale.
    private static readonly TimeSpan BatteryReadingFreshness = TimeSpan.FromSeconds(30);
    private bool _warnOnNextBatteryReading;

    /// <summary>Preset profiles, used to map the device's profile id to a display profile.</summary>
    private readonly List<TherapyProfile> _profiles = TherapyProfile.GetPresetProfiles();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedProfileName))]
    [NotifyPropertyChangedFor(nameof(ProfileSummary))]
    private TherapyProfile? _selectedProfile;

    partial void OnSelectedProfileChanged(TherapyProfile? value) => UpdateTimerDisplay(updateSemantics: true);

    /// <summary>
    /// Display name of the current therapy profile, or a prompt when none is selected.
    /// </summary>
    public string SelectedProfileName => SelectedProfile?.Name ?? "Choose a profile first";

    /// <summary>One-line summary of the current profile, e.g. "2 h sessions".</summary>
    public string ProfileSummary => SelectedProfile is { } p
        ? $"{DurationText.Short(p.TimeSession * 60)} sessions"
        : string.Empty;

    [ObservableProperty]
    private SessionStatus _sessionStatus = SessionStatus.CreateIdle();

    [ObservableProperty]
    private bool _isSessionActive;

    [ObservableProperty]
    private bool _isSessionRunning;

    [ObservableProperty]
    private bool _isSessionPaused;

    [ObservableProperty]
    private SessionRingState _ringState = SessionRingState.Idle;

    [ObservableProperty]
    private double _ringProgress;

    [ObservableProperty]
    private string _ringTimeText = string.Empty;

    [ObservableProperty]
    private string _ringAccessibleText = string.Empty;

    [ObservableProperty]
    private GloveStatusInfo _primaryGlove = new(GloveRole.Primary, GloveLinkStatus.Disconnected, null);

    [ObservableProperty]
    private GloveStatusInfo _secondaryGlove = new(GloveRole.Secondary, GloveLinkStatus.Disconnected, null);

    // One banner slot, highest priority wins (plan choice PC13; Banner README).
    [ObservableProperty]
    private bool _bannerVisible;

    [ObservableProperty]
    private StatusTone _bannerTone = StatusTone.Info;

    [ObservableProperty]
    private string? _bannerTitle;

    [ObservableProperty]
    private string? _bannerMessage;

    [ObservableProperty]
    private string? _bannerActionText;

    // Null voltage = no reading available (missing key or firmware 0.00 sentinel)
    [ObservableProperty]
    private double? _batteryPrimaryVoltage;

    [ObservableProperty]
    private double? _batterySecondaryVoltage;

    [ObservableProperty]
    private string? _sessionWarningMessage;

    [ObservableProperty]
    private string _pauseResumeText = "Pause";

    [ObservableProperty]
    private bool _isConnectionHealthy = true;

    [ObservableProperty]
    private DateTime? _lastSuccessfulPing;

    /// <summary>
    /// Single source of truth for connection state. Exposed for XAML binding.
    /// </summary>
    public IConnectionStateService ConnectionInfo { get; }

    public GloveControlViewModel(
        IGloveControlService gloveControlService,
        IBluetoothService bluetoothService,
        IDataStorageService storageService,
        IReconnectionService reconnectionService,
        IConnectionStateService connectionStateService,
        IAppLifecycleService appLifecycleService,
        IConfirmationService confirmation)
    {
        _confirmation = confirmation;
        _gloveControlService = gloveControlService;
        _bluetoothService = bluetoothService;
        _storageService = storageService;
        _reconnectionService = reconnectionService;
        ConnectionInfo = connectionStateService;

        Title = "Session";

        // Subscribe to connection events
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        // The service fetches INFO once per (re)connect; mirror its profile and
        // battery readings here
        _gloveControlService.DeviceProfileChanged += OnDeviceProfileChanged;
        _gloveControlService.DeviceInfoReceived += OnDeviceInfoReceived;

        // Session state from the service (fires on command success and every status
        // poll) — the UI must not depend on the first SESSION_STATUS poll succeeding
        _gloveControlService.SessionStateChanged += OnServiceSessionStateChanged;

        // Subscribe to centralized connection state changes
        ConnectionInfo.PropertyChanged += OnConnectionInfoPropertyChanged;
        _reconnectionService.ReconnectionStateChanged += OnReconnectionStateChanged;

        // Pause/resume BLE polling with the app window lifecycle
        _appLifecycle = appLifecycleService;
        _appLifecycle.Stopped += OnAppStopped;
        _appLifecycle.Resumed += OnAppResumed;

        // Initialize
        LoadSelectedProfileAsync().SafeFireAndForget("[GLOVECONTROL]");
        UpdateConnectionState();
        UpdateGloves();
        UpdateTimerDisplay(updateSemantics: true);
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

        // Refresh on tab visits so the battery values don't go stale. INFO carries
        // both batteries (applied by OnDeviceInfoReceived); the service serves a
        // recent reading or joins the post-connect one in flight instead of sending
        // another. No low-battery alert on mere tab visits.
        if (ConnectionInfo.IsConnected)
        {
            _gloveControlService.GetDeviceInfoAsync(BatteryReadingFreshness).SafeFireAndForget("[GLOVECONTROL]");
        }
    }

    /// <summary>Start session (the xl primary button while idle).</summary>
    [RelayCommand]
    private async Task StartSessionAsync()
    {
        if (IsSessionActive || !_sessionControls.TryAcquire())
            return;
        await ToggleSessionAsync();
    }

    /// <summary>Pause or Resume (the xl secondary button while a session runs).</summary>
    [RelayCommand]
    private async Task PauseResumeAsync()
    {
        if (!IsSessionActive || !_sessionControls.TryAcquire())
            return;
        await ToggleSessionAsync();
    }

    private async Task ToggleSessionAsync()
    {
        if (!ConnectionInfo.IsConnected)
        {
            await Shell.Current.DisplayAlert(
                "Connection lost",
                "Connect again on the Gloves tab, then try again.",
                "OK");
            return;
        }

        if (!IsSessionActive && SelectedProfile == null)
        {
            await Shell.Current.DisplayAlert(
                "Choose a profile first",
                "Go to Gloves, pick a therapy profile, then tap Apply settings.",
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
                        "Apply your profile first",
                        $"Your gloves are still using another profile. Go to Gloves and tap Apply settings to use \u201c{SelectedProfile.Name}\u201d.",
                        "OK");
                    return;
                }

                // A record kept across a reconnect whose outcome never arrived (the
                // post-connect status read failed) is superseded by this new session.
                // Read it before awaiting Start: Start's synthesized RUNNING status is
                // delivered on the main thread during the await, and would settle the
                // old record as "still active" and clear the marker first.
                var interruptedAt = _sessionDisconnectedAt;

                // Start session
                await _gloveControlService.StartSessionAsync();

                if (interruptedAt is { } disconnectedAt)
                {
                    _sessionDisconnectedAt = null;
                    SaveInterruptedSession(disconnectedAt);
                }

                // Create new therapy session
                _currentSession = new TherapySession
                {
                    StartTime = DateTime.Now,
                    ProfileUsed = SelectedProfile,
                    ProfileId = SelectedProfile!.ProfileId,
                    DeviceId = _bluetoothService.ConnectedDevice?.Id,
                    Status = SessionState.RUNNING
                };

                _showComplete = false;
                _sessionEndedMessage = null;
                StartStatusPolling();
                await UpdateSessionStatusAsync();
                SemanticScreenReader.Announce("Session started");
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
            var error = ErrorMessageHelper.GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
        }
        catch (Exception ex)
        {
            var error = ErrorMessageHelper.FromException(ex);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        if (!IsSessionActive || !_sessionControls.TryAcquire())
            return;

        // Ending a session always confirms (buzzahbuddy.md Session screen). Dismissing
        // the sheet any other way, Android back included, keeps the session going.
        var stop = await _confirmation.ConfirmAsync(new ConfirmRequest(
            "Stop this session?",
            "Your gloves stop buzzing. The time so far is saved.",
            "Stop session",
            "Keep going",
            Destructive: true));
        if (!stop || !IsSessionActive)
            return;

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
            var error = ErrorMessageHelper.GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
        }
        catch (Exception ex)
        {
            var error = ErrorMessageHelper.FromException(ex);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
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
            await ApplyBatteryReadingAsync(primaryVoltage, secondaryVoltage, warnIfLow);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Battery refresh error: {ex.Message}");
        }
    }

    private async Task ApplyBatteryReadingAsync(double? primaryVoltage, double? secondaryVoltage, bool warnIfLow)
    {
        BatteryPrimaryVoltage = primaryVoltage;
        BatterySecondaryVoltage = secondaryVoltage;
        UpdateGloves();
        // The pre-session low-battery banner replaces the old on-connect alerts; it shows
        // whenever a reading is low, so warnIfLow no longer gates anything here.
        ResolveBanner();
        await Task.CompletedTask;
    }

    /// <summary>
    /// GloveStatus for both gloves. The app only links to the primary; the secondary's
    /// state comes from the primary's report of its battery (plan choice PC15).
    /// </summary>
    private void UpdateGloves()
    {
        var link = ConnectionInfo.ConnectionState switch
        {
            ConnectionState.Connected => GloveLinkStatus.Connected,
            ConnectionState.Connecting or ConnectionState.Reconnecting => GloveLinkStatus.Connecting,
            _ => GloveLinkStatus.Disconnected,
        };
        int? Percent(double? v) => v is { } volts ? BatteryReading.ToPercentage(volts) : null;
        PrimaryGlove = new GloveStatusInfo(GloveRole.Primary, link, Percent(BatteryPrimaryVoltage));
        var secondaryLink = link != GloveLinkStatus.Connected ? GloveLinkStatus.Disconnected
            : BatterySecondaryVoltage is null ? GloveLinkStatus.NoReading
            : GloveLinkStatus.Connected;
        SecondaryGlove = new GloveStatusInfo(GloveRole.Secondary, secondaryLink, Percent(BatterySecondaryVoltage));
    }

    /// <summary>
    /// Picks the one banner the Session screen shows (plan choice PC13): session ended >
    /// reconnecting > connection unstable > low battery > status behind > session saved.
    /// </summary>
    private void ResolveBanner()
    {
        static bool Low(double? v) => v is { } volts && volts < BlueBuzzahConstants.BatteryLowThreshold;
        var primaryLow = Low(BatteryPrimaryVoltage);
        var secondaryLow = Low(BatterySecondaryVoltage);
        string? lowGlove = primaryLow && !secondaryLow ? "primary" : secondaryLow && !primaryLow ? "secondary" : null;

        (StatusTone Tone, string Title, string Message, bool GoToGloves)? banner = null;
        if (_sessionEndedMessage is { } ended)
            banner = (StatusTone.Danger, "Your session stopped", ended, _sessionEndedByConnection);
        else if (ConnectionInfo.IsReconnecting)
            banner = (StatusTone.Warning, "Reconnecting to your primary glove",
                _reconnectAttempt.Max > 0
                    ? $"Keep your phone near the glove. Try {_reconnectAttempt.Attempt} of {_reconnectAttempt.Max}."
                    : "Keep your phone near the glove.",
                true);
        else if (SessionWarningMessage == ConnectionUnstableWarning)
            banner = (StatusTone.Warning, "Connection is unstable",
                "Keep your phone close to your primary glove. The session keeps running.", false);
        else if (SessionStatus.Status == SessionState.LOW_BATTERY)
            banner = (StatusTone.Warning,
                lowGlove is null ? "A glove battery is low" : $"Your {lowGlove} glove battery is low",
                "The session keeps running. Charge the glove after this session.", false);
        else if (!IsSessionActive && ConnectionInfo.IsConnected && (primaryLow || secondaryLow))
            banner = primaryLow && secondaryLow
                ? (StatusTone.Warning, "Charge both gloves",
                    $"Primary is at {BatteryReading.ToPercentage(BatteryPrimaryVoltage!.Value)}% and secondary at {BatteryReading.ToPercentage(BatterySecondaryVoltage!.Value)}%, which may not last a full session.", false)
                : (StatusTone.Warning, $"Charge your {lowGlove} glove",
                    $"It's at {BatteryReading.ToPercentage((primaryLow ? BatteryPrimaryVoltage : BatterySecondaryVoltage)!.Value)}%, which may not last a full session.", false);
        else if (SessionWarningMessage is not null)
            banner = (StatusTone.Info, "Session time may be behind", "We're waiting for an update from your primary glove.", false);
        else if (_showComplete)
            banner = (StatusTone.Success, "Session complete", "Your session is saved.", false);

        BannerVisible = banner is not null;
        if (banner is { } b)
        {
            BannerTone = b.Tone;
            BannerTitle = b.Title;
            BannerMessage = b.Message;
            BannerActionText = b.GoToGloves ? "Go to Gloves" : null;
        }
    }

    /// <summary>
    /// INFO carries both batteries. The first reading after each connect is the one
    /// that may raise the low-battery alert, matching the old on-connect BATTERY fetch.
    /// </summary>
    private void OnDeviceInfoReceived(object? sender, GloveDeviceInfo info)
    {
        // Read synchronously, on the raising thread: only the connect reading may
        // alert. If every post-connect INFO failed, the next one is a tab visit's.
        var connectReading = _gloveControlService.IsPostConnectSyncRunning;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var warnIfLow = _warnOnNextBatteryReading && connectReading;
            _warnOnNextBatteryReading = false;
            ApplyBatteryReadingAsync(info.BatteryPrimaryVoltage, info.BatterySecondaryVoltage, warnIfLow)
                .SafeFireAndForget("[GLOVECONTROL]");
        });
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
            var polled = await _gloveControlService.GetSessionStatusAsync();

            // UNKNOWN means the reply carried no readable SESSION_STATUS (dropped,
            // truncated, or matched against the wrong frame — the protocol has no
            // correlation IDs). That is a failed poll, not a session end: assigning
            // it would clear IsSessionActive and bounce the UI back to the Start
            // button while the gloves are still running. Count it like any other
            // poll failure so the existing warn/reconnect ladder still applies.
            if (polled.Status == SessionState.UNKNOWN)
            {
                await HandlePollFailureAsync();
                return;
            }

            SessionStatus = polled;
            ResolveSessionAfterReconnect(polled);

            // Detect unexpected session end (e.g., secondary glove disconnected, error,
            // critical battery). State-aware: LOW_BATTERY keeps the session running and
            // surfaces a non-blocking warning instead of an "ended unexpectedly" alert.
            bool wasActive = _previousSessionState is SessionState.RUNNING or SessionState.PAUSED or SessionState.LOW_BATTERY or SessionState.STOPPING;
            string? endReason = SessionStatus.Status switch
            {
                SessionState.ERROR => "Your gloves reported a problem and stopped. Restart both gloves, then start a new session.",
                SessionState.CONNECTION_LOST => "Your phone lost its connection to the gloves. Move closer and connect again on the Gloves tab.",
                SessionState.CRITICAL_BATTERY => "A glove battery is almost empty. Charge both gloves, then start a new session.",
                SessionState.IDLE when SessionStatus.Progress < 100 => "Your secondary glove may have disconnected. Turn it off and on, then start a new session.",
                _ => null
            };

            if (wasActive && endReason != null && !_userRequestedStop)
            {
                // A danger banner, not a modal: it stays until the next session starts.
                _sessionEndedMessage = endReason;
                _sessionEndedByConnection = SessionStatus.Status == SessionState.CONNECTION_LOST;
                ResolveBanner();
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
            // The low-battery banner (ResolveBanner) announces itself on transition.
            if (SessionStatus.Status != SessionState.LOW_BATTERY)
            {
                SessionWarningMessage = null;
            }
            ResolveBanner();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Status update error: {ex.Message}");
            await HandlePollFailureAsync();
        }
    }

    /// <summary>
    /// Escalation ladder for a status poll that produced no usable reading — either
    /// it threw, or it returned UNKNOWN (a reply with no readable SESSION_STATUS).
    /// Both mean the same thing to the user, so both must escalate identically;
    /// keeping them in one place stops the two paths from drifting apart.
    /// </summary>
    private async Task HandlePollFailureAsync()
    {
        _consecutivePollFailures++;

        if (_consecutivePollFailures >= PollFailureReconnectThreshold)
        {
            // During a session the health-check ping is suspended (see
            // CheckConnectionHealthAsync), so the status poll owns reconnect
            // recovery. 3 failed 5s polls (~15s) forces a reconnect — faster than
            // the health check's 2x30s — and reset so it fires once per episode.
            SessionWarningMessage = null;
            IsConnectionHealthy = false;
            _consecutivePollFailures = 0;
            try
            {
                await _bluetoothService.DisconnectForReconnectAsync();
            }
            catch (Exception disconnectEx)
            {
                System.Diagnostics.Debug.WriteLine($"Status-poll reconnect failed: {disconnectEx.Message}");
            }
        }
        else if (_consecutivePollFailures >= PollFailureWarningThreshold)
        {
            SessionWarningMessage = "Session data may be outdated";
        }
        ResolveBanner();
    }

    private void UpdateSessionState()
    {
        _sessionClock.Anchor(SessionStatus, DateTime.UtcNow);

        IsSessionActive = SessionStatus.IsActive;
        IsSessionRunning = SessionStatus.IsRunning;
        IsSessionPaused = SessionStatus.IsPaused;

        PauseResumeText = IsSessionPaused ? "Resume" : "Pause";

        UpdateTimerDisplay(updateSemantics: true);

        if (IsSessionActive && !_isBackgrounded)
            StartDisplayTick();
        else
            StopDisplayTick();
    }

    private void StartStatusPolling()
    {
        StopStatusPolling();
        _statusPollCts = new CancellationTokenSource();
        RunStatusPollLoopAsync(_statusPollCts.Token).SafeFireAndForget("[GLOVECONTROL]");
    }

    /// <summary>
    /// Waits a full interval after each poll completes, so a slow poll (up to ~5.1s
    /// with the command timeout) can never overlap the next one the way an
    /// auto-repeating timer's ticks could.
    /// </summary>
    private async Task RunStatusPollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(BlueBuzzahConstants.SessionStatusPollIntervalSeconds), ct);
                await MainThread.InvokeOnMainThreadAsync(UpdateSessionStatusAsync);
            }
        }
        catch (OperationCanceledException)
        {
            // StopStatusPolling
        }
    }

    private void StopStatusPolling()
    {
        _statusPollCts?.Cancel();
        _statusPollCts?.Dispose();
        _statusPollCts = null;
    }

    /// <summary>
    /// Starts the local 500ms display tick that animates the session timer between
    /// 5s BLE status polls. Purely local — no BLE traffic.
    /// </summary>
    private void StartDisplayTick()
    {
        if (_displayTickTimer != null)
            return;
        var timer = Application.Current?.Dispatcher.CreateTimer();
        if (timer == null)
            return;
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.Tick += (s, e) => UpdateTimerDisplay(updateSemantics: false);
        timer.Start();
        _displayTickTimer = timer;
    }

    private void StopDisplayTick()
    {
        _displayTickTimer?.Stop();
        _displayTickTimer = null;
    }

    private void UpdateTimerDisplay(bool updateSemantics)
    {
        string spoken;
        if (IsSessionActive)
        {
            var reading = _sessionClock.TickTo(DateTime.UtcNow);
            RingState = IsSessionPaused ? SessionRingState.Paused : SessionRingState.Running;
            RingProgress = reading.HasKnownTotal ? reading.Fraction : 0;
            if (reading.HasKnownTotal)
            {
                var remaining = (int)Math.Ceiling(reading.Remaining.TotalSeconds);
                RingTimeText = DurationText.Clock(remaining);
                spoken = SessionRingText.Accessible(RingState, remaining);
            }
            else
            {
                var elapsed = (int)reading.Elapsed.TotalSeconds;
                RingTimeText = DurationText.Clock(elapsed);
                spoken = IsSessionPaused ? "Session paused" : SessionRingText.AccessibleCountUp(elapsed);
            }
        }
        else
        {
            // Idle shows the profile's length; Complete holds until the next start.
            var total = (SelectedProfile?.TimeSession ?? 0) * 60;
            RingState = _showComplete ? SessionRingState.Complete : SessionRingState.Idle;
            RingProgress = _showComplete ? 1 : 0;
            RingTimeText = DurationText.Clock(_showComplete ? _completedTotalSeconds : total);
            spoken = SessionRingText.Accessible(RingState, total);
        }

        // A running countdown speaks at most once a minute; other states change rarely.
        if (RingState != SessionRingState.Running || _ringSpeech.ShouldUpdate(spoken, RingState))
            RingAccessibleText = spoken;
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

        // During an active session, skip the health-check ping: the 5s session-status
        // poll already probes liveness (faster) and now drives reconnect recovery. This
        // removes redundant phone->PRIMARY commands that would otherwise compete with
        // PRIMARY<->SECONDARY sync traffic for the shared radio during therapy.
        if (IsSessionActive)
        {
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
            ResolveSessionAfterReconnect(status);
            UpdateSessionState();

            // A session we didn't start (e.g. the gloves auto-started one before the
            // app connected, surfaced by the post-connect sync) needs polling so its
            // progress advances; start it if it isn't already running.
            if (IsSessionActive && _statusPollCts == null)
            {
                StartStatusPolling();
            }
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
        _pollingPausedByLifecycle = _statusPollCts != null;
        StopStatusPolling();
        StopConnectionHealthCheck();
        StopDisplayTick();
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
                // Battery comes from the service's post-connect INFO (OnDeviceInfoReceived)
                _warnOnNextBatteryReading = true;
            }
            else
            {
                StopConnectionHealthCheck();
                IsConnectionHealthy = false;
                // Readings from the old link say nothing about the next one (which may
                // even be a different glove)
                ApplyBatteryReadingAsync(null, null, warnIfLow: false).SafeFireAndForget("[GLOVECONTROL]");
            }

            // Session teardown on disconnect. A record still kept from an earlier
            // reconnect whose outcome never arrived (UI already idle) counts too, so a
            // later disconnect can't orphan it.
            if (!isConnected && (IsSessionActive || _sessionDisconnectedAt != null))
            {
                StopStatusPolling();
                SessionStatus = SessionStatus.CreateIdle();
                UpdateSessionState();

                // ReconnectionService has already decided (synchronously, on this same
                // state change) whether to reconnect. If it is, the gloves are most
                // likely still running the session — including after the poll's own
                // forced reconnect — so don't split the history: keep the record until
                // the reconnect succeeds or gives up. Otherwise save it as incomplete.
                // Either way the earliest drop is when tracking was lost.
                var droppedAt = _sessionDisconnectedAt ?? DateTime.Now;
                if (_currentSession != null && _reconnectionService.IsReconnecting)
                {
                    _sessionDisconnectedAt = droppedAt;
                }
                else
                {
                    _sessionDisconnectedAt = null;
                    SaveInterruptedSession(droppedAt);
                }
            }

            // The first connect is announced by the Gloves tab; this covers drops and
            // reconnects. Reconnect attempts announce once, then the result (D11b).
            var announcement = state switch
            {
                ConnectionState.Connected when _wasReconnecting => "Gloves reconnected",
                ConnectionState.Disconnected or ConnectionState.Error => "Gloves disconnected",
                ConnectionState.Reconnecting when !_wasReconnecting => "Reconnecting to your primary glove",
                _ => null
            };
            _wasReconnecting = state == ConnectionState.Reconnecting
                || (_wasReconnecting && state is not (ConnectionState.Connected or ConnectionState.Disconnected));
            if (announcement != null)
            {
                SemanticScreenReader.Announce(announcement);
            }
            if (isConnected && _sessionEndedByConnection)
            {
                // Reconnected: the connection-lost banner has done its job.
                _sessionEndedMessage = null;
                _sessionEndedByConnection = false;
            }
            UpdateGloves();
            ResolveBanner();
        });
    }

    /// <summary>
    /// The first session status after a mid-session reconnect settles the kept record:
    /// still active means the session carried on and tracking continues; otherwise it
    /// ended while the phone was away and is saved as incomplete.
    /// </summary>
    private void ResolveSessionAfterReconnect(SessionStatus status)
    {
        if (_sessionDisconnectedAt is not { } disconnectedAt)
            return;

        _sessionDisconnectedAt = null;
        // A manual connect may have gone to a different glove: an active session
        // there is not the one this record tracks.
        var sameGlove = _currentSession?.DeviceId == null ||
                        _currentSession.DeviceId == _bluetoothService.ConnectedDevice?.Id;
        if (!status.IsActive || !sameGlove)
            SaveInterruptedSession(disconnectedAt);
    }

    private void OnReconnectionStateChanged(object? sender, ReconnectionStateEventArgs e)
    {
        if (e.State == ReconnectionState.Reconnecting)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _reconnectAttempt = (e.Attempt, e.MaxAttempts);
                ResolveBanner();
            });
        }

        if (e.State is not (ReconnectionState.Failed or ReconnectionState.Cancelled))
            return;

        // A manual connect replaced the reconnect (same glove, typically): keep the
        // record for that connect to settle - its post-connect status resolves it, and
        // a failed connect saves it through the disconnect teardown.
        if (e.SupersededByConnect)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // The reconnect gave up (or the user moved on): the session can no longer
            // be followed, so record it as ending when the link dropped.
            if (_sessionDisconnectedAt is { } disconnectedAt)
            {
                _sessionDisconnectedAt = null;
                SaveInterruptedSession(disconnectedAt);
            }
        });
    }

    private void SaveInterruptedSession(DateTime endTime)
    {
        if (_currentSession == null)
            return;

        _currentSession.EndTime = endTime;
        _currentSession.IsCompleted = false;
        _storageService.SaveSessionAsync(_currentSession).SafeFireAndForget("[GLOVECONTROL]");
        _currentSession = null;
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
            SemanticScreenReader.Announce("Session complete");

            // One optional note in the person's own words (no effectiveness rating:
            // the app makes no outcome claims).
            var notes = await Shell.Current.DisplayPromptAsync(
                "Add a note?",
                "Write anything you want to remember about this session. It stays on this phone.",
                accept: "Save note",
                cancel: "Skip",
                placeholder: "For example: Tremor felt lighter after lunch",
                maxLength: 500,
                keyboard: Keyboard.Text);

            if (_currentSession != null)
            {
                _currentSession.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
                await _storageService.SaveSessionAsync(_currentSession);
            }

            // Reset session; the ring holds Complete and a success banner confirms the save.
            _currentSession = null;
            _completedTotalSeconds = SessionStatus.TotalTimeSeconds;
            _showComplete = true;
            SessionStatus = SessionStatus.CreateIdle();
            UpdateSessionState();
            ResolveBanner();
        });
    }

    private void OnConnectionInfoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IConnectionStateService.IsReconnecting) or nameof(IConnectionStateService.ConnectionState))
        {
            UpdateGloves();
            ResolveBanner();
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
            _gloveControlService.DeviceInfoReceived -= OnDeviceInfoReceived;
            _gloveControlService.SessionStateChanged -= OnServiceSessionStateChanged;
            ConnectionInfo.PropertyChanged -= OnConnectionInfoPropertyChanged;
            _reconnectionService.ReconnectionStateChanged -= OnReconnectionStateChanged;
            _appLifecycle.Stopped -= OnAppStopped;
            _appLifecycle.Resumed -= OnAppResumed;
            StopStatusPolling();
            StopConnectionHealthCheck();
            StopDisplayTick();
            System.Diagnostics.Debug.WriteLine("[GLOVECONTROL] ViewModel disposed, unsubscribed from events");
        }
        base.Dispose(disposing);
    }
}
