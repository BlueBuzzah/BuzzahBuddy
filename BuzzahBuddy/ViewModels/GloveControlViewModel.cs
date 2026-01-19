using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;
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
    private TherapySession? _currentSession;
    private System.Timers.Timer? _statusPollTimer;
    private System.Timers.Timer? _healthCheckTimer;

    [ObservableProperty]
    private ObservableCollection<ProfileItemViewModel> _availableProfiles = new();

    [ObservableProperty]
    private TherapyProfile? _selectedProfile;

    [ObservableProperty]
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

    partial void OnIsShowingAdvancedProfilesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowMoreButtonText));
    }

    [ObservableProperty]
    private SessionStatus _sessionStatus = SessionStatus.CreateIdle();

    [ObservableProperty]
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
        $"Primary battery: {BatteryPrimaryPercentage} percent, {GetBatteryStatusText(BatteryPrimaryVoltage)}";

    /// <summary>
    /// Accessibility description for the secondary battery status.
    /// </summary>
    public string BatterySecondaryDescription =>
        $"Secondary battery: {BatterySecondaryPercentage} percent, {GetBatteryStatusText(BatterySecondaryVoltage)}";

    [ObservableProperty]
    private bool _showBatteryRefresh = true;

    [ObservableProperty]
    private bool _isLoadingProfile;

    [ObservableProperty]
    private bool _isRefreshingBattery;

    [ObservableProperty]
    private bool _isTestingConnection;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _sessionButtonText = "Start Session";

    [ObservableProperty]
    private string _sessionButtonDescription = "Starts a new therapy session";

    [ObservableProperty]
    private bool _isConnectionHealthy = true;

    [ObservableProperty]
    private DateTime? _lastSuccessfulPing;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private string? _connectedDeviceName;

    public GloveControlViewModel(
        IGloveControlService gloveControlService,
        IBluetoothService bluetoothService,
        IDataStorageService storageService)
    {
        _gloveControlService = gloveControlService;
        _bluetoothService = bluetoothService;
        _storageService = storageService;

        Title = "Therapy Control";

        // Subscribe to connection events
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        // Initialize
        _ = LoadProfilesAsync();
        UpdateConnectionState();
    }

    [RelayCommand]
    private async Task ToggleSessionAsync()
    {
        if (!IsConnected)
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
            }
            else if (IsSessionRunning)
            {
                // Pause session
                await _gloveControlService.PauseSessionAsync();
                await UpdateSessionStatusAsync();
            }
            else if (IsSessionPaused)
            {
                // Resume session
                await _gloveControlService.ResumeSessionAsync();
                await UpdateSessionStatusAsync();
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
    private void SelectProfile(ProfileItemViewModel profileItem)
    {
        if (profileItem?.Profile == null)
            return;

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
    private async Task TestConnectionAsync()
    {
        if (!IsConnected)
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
        if (!IsConnected)
            return;

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
            BatteryPrimaryPercentage = VoltageToPercentage(primaryVoltage);
            BatterySecondaryPercentage = VoltageToPercentage(secondaryVoltage);

            // Debug: Log calculated percentages
            System.Diagnostics.Debug.WriteLine($"[BATTERY] Percentages - Primary: {BatteryPrimaryPercentage}%, Secondary: {BatterySecondaryPercentage}%");

            // Update colors based on voltage thresholds
            BatteryPrimaryColor = GetBatteryColor(primaryVoltage);
            BatterySecondaryColor = GetBatteryColor(secondaryVoltage);

            // Notify accessibility description properties
            OnPropertyChanged(nameof(BatteryPrimaryDescription));
            OnPropertyChanged(nameof(BatterySecondaryDescription));

            // Progressive disclosure: Hide refresh button when battery is good
            var minPercentage = Math.Min(BatteryPrimaryPercentage, BatterySecondaryPercentage);
            ShowBatteryRefresh = minPercentage <= 50;

            // Check for low battery warnings
            await CheckBatteryWarningAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Battery refresh error: {ex.Message}");
        }
        finally
        {
            IsRefreshingBattery = false;
            IsBusy = false;
        }
    }

    private static int VoltageToPercentage(double voltage)
    {
        const double minVoltage = 3.0;
        const double maxVoltage = 4.2;

        if (voltage <= minVoltage) return 0;
        if (voltage >= maxVoltage) return 100;

        return (int)((voltage - minVoltage) / (maxVoltage - minVoltage) * 100);
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
        }
    }

    private async Task UpdateSessionStatusAsync()
    {
        if (!IsConnected)
        {
            SessionStatus = SessionStatus.CreateIdle();
            UpdateSessionState();
            return;
        }

        try
        {
            var previousProgress = SessionStatus.Progress;
            SessionStatus = await _gloveControlService.GetSessionStatusAsync();

            // Update current session tracking
            if (_currentSession != null && SessionStatus.IsActive)
            {
                _currentSession.ElapsedTimeSeconds = SessionStatus.ElapsedTimeSeconds;
                _currentSession.Progress = SessionStatus.Progress;
                _currentSession.Status = SessionStatus.Status;
            }

            // Check for session completion
            if (SessionStatus.Progress >= 1.0 && previousProgress < 1.0)
            {
                await HandleSessionCompletionAsync();
            }

            UpdateSessionState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Status update error: {ex.Message}");
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
        _statusPollTimer = new System.Timers.Timer(BlueBuzzahConstants.SessionStatusPollIntervalSeconds * 1000);
        _statusPollTimer.Elapsed += async (s, e) => await UpdateSessionStatusAsync();
        _statusPollTimer.Start();
    }

    private void StopStatusPolling()
    {
        _statusPollTimer?.Stop();
        _statusPollTimer = null;
    }

    private void StartConnectionHealthCheck()
    {
        _healthCheckTimer?.Stop();
        _healthCheckTimer = new System.Timers.Timer(BlueBuzzahConstants.ConnectionHealthCheckIntervalSeconds * 1000);
        _healthCheckTimer.Elapsed += async (s, e) => await CheckConnectionHealthAsync();
        _healthCheckTimer.Start();
    }

    private void StopConnectionHealthCheck()
    {
        _healthCheckTimer?.Stop();
        _healthCheckTimer = null;
    }

    private async Task CheckConnectionHealthAsync()
    {
        if (!IsConnected)
        {
            IsConnectionHealthy = false;
            return;
        }

        try
        {
            var pingSuccess = await _gloveControlService.PingAsync(timeoutMs: BlueBuzzahConstants.PingTimeoutMs);

            if (pingSuccess)
            {
                IsConnectionHealthy = true;
                LastSuccessfulPing = DateTime.Now;
            }
            else
            {
                IsConnectionHealthy = false;
                System.Diagnostics.Debug.WriteLine("Connection health check: PING failed");
            }
        }
        catch (Exception ex)
        {
            IsConnectionHealthy = false;
            System.Diagnostics.Debug.WriteLine($"Connection health check error: {ex.Message}");
        }
    }

    private void UpdateConnectionState()
    {
        ConnectionState = _bluetoothService.CurrentConnectionState;
        IsConnected = ConnectionState == ConnectionState.Connected;
        ConnectedDeviceName = _bluetoothService.ConnectedDevice?.Name;

        if (IsConnected)
        {
            _ = RefreshBatteryAsync();
            StartConnectionHealthCheck();
        }
        else
        {
            StopConnectionHealthCheck();
            IsConnectionHealthy = false;
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectionState = state;
            IsConnected = state == ConnectionState.Connected;
            ConnectedDeviceName = _bluetoothService.ConnectedDevice?.Name;

            if (IsConnected)
            {
                StartConnectionHealthCheck();
            }
            else
            {
                StopConnectionHealthCheck();
                IsConnectionHealthy = false;
            }

            // Stop session if disconnected
            if (!IsConnected && IsSessionActive)
            {
                StopStatusPolling();
                SessionStatus = SessionStatus.CreateIdle();
                UpdateSessionState();

                if (_currentSession != null)
                {
                    _currentSession.EndTime = DateTime.Now;
                    _currentSession.IsCompleted = false; // Incomplete due to disconnection
                }
            }
        });
    }

    private static Color GetBatteryColor(double voltage)
    {
        if (voltage > BlueBuzzahConstants.BatteryGoodThreshold) return Colors.Green;
        if (voltage >= BlueBuzzahConstants.BatteryMediumThreshold) return Colors.Orange;
        return Colors.Red;
    }

    private static string GetBatteryStatusText(double voltage)
    {
        if (voltage > BlueBuzzahConstants.BatteryGoodThreshold) return "good";
        if (voltage >= BlueBuzzahConstants.BatteryMediumThreshold) return "low";
        return "critical";
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

    /// <summary>
    /// Unsubscribes from Bluetooth service events and stops timers to prevent memory leaks.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
            StopStatusPolling();
            StopConnectionHealthCheck();
            System.Diagnostics.Debug.WriteLine("[GLOVECONTROL] ViewModel disposed, unsubscribed from events");
        }
        base.Dispose(disposing);
    }
}
