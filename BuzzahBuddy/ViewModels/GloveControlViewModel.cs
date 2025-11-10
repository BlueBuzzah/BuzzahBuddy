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
    private ObservableCollection<TherapyProfile> _availableProfiles = new();

    [ObservableProperty]
    private TherapyProfile? _selectedProfile;

    [ObservableProperty]
    private SessionStatus _sessionStatus = SessionStatus.CreateIdle();

    [ObservableProperty]
    private bool _isSessionActive;

    [ObservableProperty]
    private bool _isSessionRunning;

    [ObservableProperty]
    private bool _isSessionPaused;

    [ObservableProperty]
    private double _batteryLeftVoltage;

    [ObservableProperty]
    private double _batteryRightVoltage;

    [ObservableProperty]
    private string _batteryLeftColor = "Green";

    [ObservableProperty]
    private string _batteryRightColor = "Green";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _sessionButtonText = "Start Session";

    [ObservableProperty]
    private string _pauseButtonText = "Pause";

    [ObservableProperty]
    private bool _isConnectionHealthy = true;

    [ObservableProperty]
    private DateTime? _lastSuccessfulPing;

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

        if (SelectedProfile == null)
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
            if (IsSessionActive)
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
            else
            {
                // Start session
                await _gloveControlService.StartSessionAsync();

                // Create new therapy session
                _currentSession = new TherapySession
                {
                    StartTime = DateTime.Now,
                    ProfileUsed = SelectedProfile,
                    ProfileId = SelectedProfile.ProfileId,
                    DeviceId = _bluetoothService.ConnectedDevice?.Id,
                    Status = SessionState.RUNNING
                };

                StartStatusPolling();
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
    private async Task PauseResumeSessionAsync()
    {
        if (!IsConnected || !IsSessionActive)
            return;

        IsBusy = true;

        try
        {
            if (IsSessionPaused)
            {
                await _gloveControlService.ResumeSessionAsync();
            }
            else
            {
                await _gloveControlService.PauseSessionAsync();
            }

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
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshBatteryAsync()
    {
        if (!IsConnected)
            return;

        IsBusy = true;

        try
        {
            var (leftVoltage, rightVoltage) = await _gloveControlService.GetBatteryAsync();
            BatteryLeftVoltage = leftVoltage;
            BatteryRightVoltage = rightVoltage;

            // Update colors based on voltage thresholds
            BatteryLeftColor = GetBatteryColor(leftVoltage);
            BatteryRightColor = GetBatteryColor(rightVoltage);

            // Check for low battery warnings
            await CheckBatteryWarningAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Battery refresh error: {ex.Message}");
        }
        finally
        {
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

                // Revert to previous selection
                // (Note: This is a simplification; in a real app you'd track the previous selection)
            });
            return;
        }

        // Load the selected profile
        _ = Task.Run(async () =>
        {
            try
            {
                await _gloveControlService.LoadProfileAsync(value.ProfileId);
                await _storageService.SaveLastProfileAsync(value.ProfileId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Profile load error: {ex.Message}");
            }
        });
    }

    private async Task LoadProfilesAsync()
    {
        try
        {
            var profiles = await _gloveControlService.ListProfilesAsync();

            AvailableProfiles.Clear();
            foreach (var profile in profiles)
            {
                AvailableProfiles.Add(profile);
            }

            // Load last used profile or default to Noisy VCR (profile 2)
            var lastProfileId = await _storageService.GetLastProfileAsync();
            SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.ProfileId == lastProfileId)
                           ?? AvailableProfiles.FirstOrDefault(p => p.ProfileId == 2)
                           ?? AvailableProfiles.FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Load profiles error: {ex.Message}");

            // Fallback to preset profiles
            var presetProfiles = TherapyProfile.GetPresetProfiles();
            AvailableProfiles.Clear();
            foreach (var profile in presetProfiles)
            {
                AvailableProfiles.Add(profile);
            }

            SelectedProfile = AvailableProfiles.FirstOrDefault(p => p.ProfileId == 2);
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

        SessionButtonText = IsSessionActive ? "Stop Session" : "Start Session";
        PauseButtonText = IsSessionPaused ? "Resume" : "Pause";
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
        IsConnected = _bluetoothService.CurrentConnectionState == ConnectionState.Connected;

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
            IsConnected = state == ConnectionState.Connected;

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

    private static string GetBatteryColor(double voltage)
    {
        if (voltage > BlueBuzzahConstants.BatteryGoodThreshold) return "Green";
        if (voltage >= BlueBuzzahConstants.BatteryMediumThreshold) return "Yellow";
        return "Red";
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
        if (BatteryLeftVoltage > 0 && BatteryLeftVoltage < BlueBuzzahConstants.BatteryLowThreshold)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.DisplayAlert(
                    "Low Battery Warning",
                    $"Left glove battery is low ({BatteryLeftVoltage:F2}V). Consider charging before starting a session.",
                    "OK");
            });
        }

        if (BatteryRightVoltage > 0 && BatteryRightVoltage < BlueBuzzahConstants.BatteryLowThreshold)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Shell.Current.DisplayAlert(
                    "Low Battery Warning",
                    $"Right glove battery is low ({BatteryRightVoltage:F2}V). Consider charging before starting a session.",
                    "OK");
            });
        }
    }
}
