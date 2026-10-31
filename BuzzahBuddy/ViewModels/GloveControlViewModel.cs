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
    // The "Session Complete" banner's message: claims a save only when one happened.
    private string _completeMessage = string.Empty;
    private int _completedTotalSeconds;
    private string? _sessionEndedMessage;
    private bool _sessionEndedByConnection;
    private bool _wasReconnecting;
    private (int Attempt, int Max) _reconnectAttempt;
    private readonly HistoryStore _history;
    // The session being tracked (persisted, so it survives the app being killed or the
    // link dropping), and its Demo Mode twin, which is never persisted or saved. Kept
    // apart so demo readings can never close a real session.
    private OpenSession? _openSession;
    private OpenSession? _demoSession;
    private CancellationTokenSource? _statusPollCts;
    private System.Timers.Timer? _healthCheckTimer;
    private readonly SessionClock _sessionClock = new();
    private IDispatcherTimer? _displayTickTimer;
    private readonly IAppLifecycleService _appLifecycle;
    private bool _pollingPausedByLifecycle;
    private bool _isBackgrounded;
    private SessionState _previousSessionState = SessionState.IDLE;
    private bool _userRequestedStop;
    // Older firmware: a session that ended while the phone was away reads RUNNING with TOTAL 0
    // after the reconnect, on every poll. Once such a ghost has closed the tracked session the
    // ghosts that follow are that finished session, so the UI reads them as idle until a real
    // IDLE or a trackable session arrives. TOTAL 0 isn't idle in general: older firmware that
    // never reports TOTAL still counts up when no session was being tracked.
    private bool _ghostAfterClose;
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
    [NotifyPropertyChangedFor(nameof(ProfileSummarySpoken))]
    [NotifyPropertyChangedFor(nameof(ProfileCardSpoken))]
    private TherapyProfile? _selectedProfile;

    partial void OnSelectedProfileChanged(TherapyProfile? value) => UpdateTimerDisplay(updateSemantics: true);

    /// <summary>
    /// Display name of the current therapy profile, or a prompt when none is selected.
    /// </summary>
    public string SelectedProfileName => SelectedProfile?.Name ?? "Choose a Profile First";

    /// <summary>One-line summary of the current profile, e.g. "2h sessions".</summary>
    public string ProfileSummary => SelectedProfile is { } p
        ? $"{DurationText.Short(p.TimeSession * 60)} sessions"
        : string.Empty;

    /// <summary><see cref="ProfileSummary"/> for screen readers: "2 hours sessions".</summary>
    public string ProfileSummarySpoken => SelectedProfile is { } p
        ? $"{DurationText.Spoken(p.TimeSession * 60)} sessions"
        : string.Empty;

    /// <summary>The profile card as one screen-reader element.</summary>
    public string ProfileCardSpoken => SelectedProfile is null
        ? $"Therapy profile. {SelectedProfileName}"
        : $"Therapy profile, {SelectedProfileName}, {ProfileSummarySpoken}";

    /// <summary>The profile card's hint: what a tap does, or why it does nothing.</summary>
    public string ProfileCardHint => CanChangeProfile ? "Opens the Gloves tab to change it" : "Stop your session to change it";

    /// <summary>The profile is locked while a session runs or is paused, as on Gloves.</summary>
    public bool CanChangeProfile => !IsSessionActive;

    [ObservableProperty]
    private SessionStatus _sessionStatus = SessionStatus.CreateIdle();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeProfile))]
    [NotifyPropertyChangedFor(nameof(ProfileCardHint))]
    [NotifyCanExecuteChangedFor(nameof(ChangeProfileCommand))]
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
        IConfirmationService confirmation,
        HistoryStore history)
    {
        _confirmation = confirmation;
        _gloveControlService = gloveControlService;
        _bluetoothService = bluetoothService;
        _storageService = storageService;
        _reconnectionService = reconnectionService;
        _history = history;
        ConnectionInfo = connectionStateService;

        Title = "Session";

        // Before any event can deliver a reading that continues or ends it.
        _openSession = _storageService.LoadOpenSession();

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
                "Connection Lost",
                "Connect again on the Gloves tab, then try again.",
                "OK");
            return;
        }

        if (!IsSessionActive && SelectedProfile == null)
        {
            await Shell.Current.DisplayAlert(
                "Choose a Profile First",
                "Go to Gloves, pick a therapy profile, then tap Apply Settings.",
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
                        "Apply Your Profile First",
                        $"Your gloves are still using another profile. Go to Gloves and tap Apply Settings to use \u201c{SelectedProfile.Name}\u201d.",
                        "OK");
                    return;
                }

                // Start session. Its synthesized RUNNING status reaches TrackSession
                // through the service event, which begins tracking.
                await _gloveControlService.StartSessionAsync();

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
        // Demo Mode sessions aren't saved, so the sheet doesn't promise it.
        var stop = await _confirmation.ConfirmAsync(new ConfirmRequest(
            "Stop This Session?",
            IsDemoMode ? "Your gloves stop buzzing." : "Your gloves stop buzzing. The time so far is saved.",
            "Stop Session",
            "Keep Going",
            Destructive: true));
        if (!stop || !IsSessionActive)
            return;

        IsBusy = true;

        try
        {
            // Stop session. The IDLE reading that follows closes the record as
            // stopped early (TrackSession), because this flag is set first.
            _userRequestedStop = true;
            await _gloveControlService.StopSessionAsync();

            StopStatusPolling();
            await UpdateSessionStatusAsync();
            SemanticScreenReader.Announce("Session stopped");
        }
        catch (BlueBuzzahCommandException ex)
        {
            // The session carries on: a later natural end isn't a stop.
            _userRequestedStop = false;
            var error = ErrorMessageHelper.GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
        }
        catch (Exception ex)
        {
            _userRequestedStop = false;
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

    /// <summary>The profile card's tap: profiles are picked on Gloves. Off during a session.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeProfile))]
    private Task ChangeProfileAsync() => NavigateToDevicesAsync();

    [RelayCommand]
    private async Task ScanForGlovesAsync()
    {
        // Single-use parameters: tapping the Gloves tab later must not rescan.
        await Shell.Current.GoToAsync(Routes.Devices, new ShellNavigationQueryParameters { [DeviceListViewModel.ScanQueryKey] = true });
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
        static bool Low(double? v) => GloveStatusText.IsLowVoltage(v);
        var primaryLow = Low(BatteryPrimaryVoltage);
        var secondaryLow = Low(BatterySecondaryVoltage);
        string? lowGlove = primaryLow && !secondaryLow ? "Primary" : secondaryLow && !primaryLow ? "Secondary" : null;

        (StatusTone Tone, string Title, string Message, bool GoToGloves)? banner = null;
        if (_sessionEndedMessage is { } ended)
            banner = (StatusTone.Danger, "Your Session Stopped", ended, false);
        else if (ConnectionInfo.IsReconnecting)
            banner = (StatusTone.Warning, "Reconnecting to Your Primary Glove",
                _reconnectAttempt.Max > 0
                    ? $"Keep your phone near the glove. Try {_reconnectAttempt.Attempt} of {_reconnectAttempt.Max}."
                    : "Keep your phone near the glove.",
                true);
        else if (SessionWarningMessage == ConnectionUnstableWarning)
            banner = (StatusTone.Warning, "Connection Is Unstable",
                "Keep your phone close to your primary glove. The session keeps running.", false);
        else if (SessionStatus.Status == SessionState.LOW_BATTERY)
            banner = (StatusTone.Warning,
                lowGlove is null ? "A Glove Battery Is Low" : $"Your {lowGlove} Glove Battery Is Low",
                "The session keeps running. Charge the glove after this session.", false);
        else if (!IsSessionActive && ConnectionInfo.IsConnected && (primaryLow || secondaryLow))
            banner = primaryLow && secondaryLow
                ? (StatusTone.Warning, "Charge Both Gloves",
                    $"Primary is at {BatteryReading.ToPercentage(BatteryPrimaryVoltage!.Value)}% and secondary at {BatteryReading.ToPercentage(BatterySecondaryVoltage!.Value)}%, which may not last a full session.", false)
                : (StatusTone.Warning, $"Charge Your {lowGlove} Glove",
                    $"It's at {BatteryReading.ToPercentage((primaryLow ? BatteryPrimaryVoltage : BatterySecondaryVoltage)!.Value)}%, which may not last a full session.", false);
        else if (SessionWarningMessage is not null)
            banner = (StatusTone.Info, "Session Time May Be Behind", "We're waiting for an update from your primary glove.", false);
        else if (_showComplete)
            banner = (StatusTone.Success, "Session Complete", _completeMessage, false);

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
            TrackSession(polled, fromPoll: true);
            SessionStatus = ForDisplay(polled);
            // A poll already in flight when Stop was confirmed still reads the session
            // running; keep the flag for the IDLE reading that Stop produces.
            if (!polled.IsActive)
                _userRequestedStop = false;

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

        // A running countdown speaks at most once a minute. Every state goes through the throttle so
        // it sees pauses and resumes.
        if (_ringSpeech.ShouldUpdate(spoken, RingState))
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
            // The poll's GetSessionStatusAsync fires this event with the very status it
            // then assigns to SessionStatus. Skipping that echo is best-effort: on the
            // main thread this callback runs inline, before the poll assigns it, so a
            // poll reading can reach TrackSession twice. TrackSession is idempotent for
            // a repeated reading (only the poll pass observes; the first pass to see an
            // end closes it and the second finds nothing open).
            if (ReferenceEquals(status, SessionStatus))
                return;

            SessionStatus = status;
            TrackSession(status, fromPoll: false);
            SessionStatus = ForDisplay(status);
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

            // Session UI teardown on disconnect. The open session is kept: the gloves
            // carry on without the phone, and the next reading continues or ends it.
            if (!isConnected && IsSessionActive)
            {
                StopStatusPolling();
                SessionStatus = SessionStatus.CreateIdle();
                UpdateSessionState();
            }

            // The first connect is announced by the Gloves tab; this covers drops and
            // reconnects. Reconnect attempts announce once, then the result (D11b).
            var announcement = state switch
            {
                ConnectionState.Connected when _wasReconnecting => "Gloves reconnected",
                ConnectionState.Disconnected or ConnectionState.Error => "Gloves disconnected",
                ConnectionState.Reconnecting when !_wasReconnecting => "Reconnecting to Your Primary Glove",
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

    private static bool IsDemoMode => Preferences.Default.Get(SwitchableBluetoothService.DemoModeKey, false);

    /// <summary>
    /// The one place a reading begins, continues or ends the tracked session, and the only
    /// place that decides end-of-session UI. Only poll readings may <see cref="OpenSession.Observe"/>:
    /// the service synthesizes Pause/Resume from the last poll's ELAPSED (up to 5 s stale), so
    /// letting them observe would count a whole pause as therapy time on older firmware, whose
    /// ELAPSED runs through pauses.
    /// </summary>
    private void TrackSession(SessionStatus status, bool fromPoll)
    {
        var demo = IsDemoMode;
        ref var open = ref (demo ? ref _demoSession : ref _openSession);
        var now = DateTimeOffset.Now;
        var device = _bluetoothService.ConnectedDevice?.Id;
        var wasActive = _previousSessionState is SessionState.RUNNING or SessionState.PAUSED or SessionState.LOW_BATTERY or SessionState.STOPPING;
        _previousSessionState = status.Status;
        if (status.Status == SessionState.IDLE || OpenSession.CanTrack(status))
            _ghostAfterClose = false;

        if (open != null)
        {
            if (open.Continues(status, device, now))
            {
                if (fromPoll)
                {
                    open.Observe(status, now);
                    if (!demo)
                        _storageService.SaveOpenSession(_openSession);
                }
                return;
            }

            if (!demo && IsGhost(status))
                _ghostAfterClose = true;
            var (outcome, record) = open.End(status.LastSession, _userRequestedStop, now);
            var endReason = open.IsSummaryOfThis(status.LastSession) ? status.LastSession!.Reason : (SessionEndReason?)null;
            _userRequestedStop = false;
            open = null;
            if (!demo)
            {
                if (record != null)
                    SaveClosedSessionAsync(record).SafeFireAndForget("[GLOVECONTROL]");
                else
                    _storageService.SaveOpenSession(null);
            }
            // A reading that is itself a new session (the gloves started another one while
            // the phone was away) goes straight to that session's UI; the old one is saved.
            if (wasActive && !OpenSession.CanTrack(status))
                EndSessionUi(outcome, record, status, endReason);
        }

        if (OpenSession.CanTrack(status))
        {
            var profileId = _gloveControlService.DeviceProfileId;
            open = OpenSession.Begin(status, now, profileId, ProfileNameFor(profileId), device);
            if (!demo)
                _storageService.SaveOpenSession(_openSession);
        }
    }

    private static bool IsGhost(SessionStatus status) => status.IsActive && status.TotalTimeSeconds == 0;

    /// <summary>What the session UI shows for a reading: a ghost after a close reads idle.</summary>
    private SessionStatus ForDisplay(SessionStatus status) =>
        _ghostAfterClose && IsGhost(status) ? SessionStatus.CreateIdle() : status;

    /// <summary>
    /// Writes a closed session to history, and only then drops its persisted open copy: if the
    /// write fails, the copy reloads at the next launch and the next reading closes it again.
    /// </summary>
    private async Task SaveClosedSessionAsync(SessionRecord record)
    {
        try
        {
            await _history.AddSessionAsync(record);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GLOVECONTROL] Could not save session {record.Id}, keeping it open: {ex.Message}");
            return;
        }
        // The reading that closed it may already have begun and persisted a new session.
        if (_storageService.LoadOpenSession()?.Id == record.Id)
            _storageService.SaveOpenSession(null);
    }

    private void EndSessionUi(SessionOutcome outcome, SessionRecord? record, SessionStatus status, SessionEndReason? endReason)
    {
        switch (outcome)
        {
            case SessionOutcome.Completed:
                HandleSessionCompletionAsync(record).SafeFireAndForget("[GLOVECONTROL]");
                break;
            case SessionOutcome.EndedEarly:
                // A danger banner, not a modal: it stays until the next session starts.
                _sessionEndedMessage = EndReasonFor(status, endReason);
                _sessionEndedByConnection = status.Status == SessionState.CONNECTION_LOST;
                ResolveBanner();
                break;
            // StoppedEarly: Stop already announces.
        }
    }

    private static string? EndReasonFor(SessionStatus status, SessionEndReason? endReason) => endReason switch
    {
        // The gloves said why: a power switch needs no banner.
        SessionEndReason.Error => EndReasonFor(SessionState.ERROR, status.Progress),
        SessionEndReason.ConnectionLost => EndReasonFor(SessionState.CONNECTION_LOST, status.Progress),
        SessionEndReason.Battery => EndReasonFor(SessionState.CRITICAL_BATTERY, status.Progress),
        null or SessionEndReason.Unknown => EndReasonFor(status.Status, status.Progress),
        _ => null
    };

    private static string? EndReasonFor(SessionState state, int progress) => state switch
    {
        SessionState.ERROR => "Your gloves reported a problem and stopped. Restart both gloves, then start a new session.",
        SessionState.CONNECTION_LOST => "Your gloves lost their connection to each other. Keep them close together, restart both, then start a new session.",
        SessionState.CRITICAL_BATTERY => "A glove battery is almost empty. Charge both gloves, then start a new session.",
        SessionState.IDLE when progress < 100 => "Your secondary glove may have disconnected. Turn it off and on, then start a new session.",
        _ => null
    };

    private string ProfileNameFor(int profileId) =>
        _profiles.FirstOrDefault(p => p.ProfileId == profileId)?.Name
        ?? SelectedProfile?.Name
        ?? "Unknown";

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
    }

    private async Task HandleSessionCompletionAsync(SessionRecord? record)
    {
        // Stop polling and cleanup
        StopStatusPolling();

        // Run on UI thread
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            SemanticScreenReader.Announce("Session complete");

            // One optional note in the person's own words (no effectiveness rating:
            // the app makes no outcome claims). Only for a saved session: the prompt
            // promises the note is kept.
            if (record != null && !IsDemoMode)
            {
                var notes = await Shell.Current.DisplayPromptAsync(
                    "Add a Note?",
                    "Write anything you want to remember about this session. It stays on this phone.",
                    accept: "Save Note",
                    cancel: "Skip",
                    placeholder: "For example: Tremor felt lighter after lunch",
                    maxLength: HistoryStore.MaxNoteLength,
                    keyboard: Keyboard.Text);

                if (!string.IsNullOrWhiteSpace(notes))
                {
                    try
                    {
                        await _history.AppendDayNoteAsync(record.Day, notes);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        System.Diagnostics.Debug.WriteLine($"[GLOVECONTROL] Could not save note: {ex.Message}");
                    }
                }
            }

            // Reset session; the ring holds Complete and a success banner confirms the save.
            _completedTotalSeconds = record?.PlannedSeconds ?? SessionStatus.TotalTimeSeconds;
            _completeMessage = IsDemoMode ? "Demo Mode sessions aren't saved."
                : record != null ? "Your session is saved."
                : string.Empty;
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
