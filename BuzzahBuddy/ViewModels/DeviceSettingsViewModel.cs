using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using static BuzzahBuddy.Services.Glove.ErrorMessageHelper;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the device-settings section of the Device page.
/// Owns settings that live on the connected glove: therapy profile, therapy LED,
/// battery status, and connection diagnostics. Device settings are written to the
/// glove at the moment they are changed and are unavailable when disconnected.
/// </summary>
public partial class DeviceSettingsViewModel : BaseViewModel
{
    private readonly IGloveControlService _gloveControlService;
    private readonly IBluetoothService _bluetoothService;
    private readonly IDataStorageService _storageService;
    private readonly IReconnectionService _reconnectionService;

    private const string ProfileRebootWarning = "Gloves are restarting to apply the new profile…";

    /// <summary>Suppresses pending-change tracking while syncing the LED toggle from the device.</summary>
    private bool _suppressTherapyLedWrite;

    /// <summary>LED state as last read from (or successfully written to) the device.</summary>
    private bool _deviceTherapyLedOff;

    /// <summary>
    /// Centralized connection state service exposed for XAML binding.
    /// </summary>
    public IConnectionStateService ConnectionInfo { get; }

    [ObservableProperty]
    private ObservableCollection<ProfileItemViewModel> _availableProfiles = new();

    [ObservableProperty]
    private TherapyProfile? _selectedProfile;

    [ObservableProperty]
    private string? _profileStatusMessage;

    /// <summary>
    /// True while a therapy session is running or paused. Profile changes are
    /// blocked during a session.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSessionInactive))]
    private bool _isSessionActive;

    /// <summary>Inverse of IsSessionActive for XAML binding.</summary>
    public bool IsSessionInactive => !IsSessionActive;

    /// <summary>
    /// True when the selection on this page differs from what's on the gloves —
    /// enables the Apply Settings button.
    /// </summary>
    public bool HasPendingChanges => IsLedDirty || IsProfileDirty;

    private bool IsLedDirty => TherapyLedLoaded && TherapyLedOff != _deviceTherapyLedOff;

    private bool IsProfileDirty =>
        ConnectionInfo.IsConnected
        && _gloveControlService.DeviceProfileId > 0
        && SelectedProfile != null
        && SelectedProfile.ProfileId != _gloveControlService.DeviceProfileId;

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

    /// <summary>Display text for the primary battery, e.g. "60% (3.72V)" or "—".</summary>
    public string BatteryPrimaryText => BatteryReading.Format(BatteryPrimaryVoltage);

    /// <summary>Display text for the secondary battery, e.g. "58% (3.68V)" or "—".</summary>
    public string BatterySecondaryText => BatteryReading.Format(BatterySecondaryVoltage);

    /// <summary>Accessibility description for the primary battery status.</summary>
    public string BatteryPrimaryDescription =>
        BatteryPrimaryVoltage is { } v
            ? $"Primary battery: {BatteryReading.ToPercentage(v)} percent, {BatteryReading.GetBatteryStatusText(v)}"
            : "Primary battery: status unavailable";

    /// <summary>Accessibility description for the secondary battery status.</summary>
    public string BatterySecondaryDescription =>
        BatterySecondaryVoltage is { } v
            ? $"Secondary battery: {BatteryReading.ToPercentage(v)} percent, {BatteryReading.GetBatteryStatusText(v)}"
            : "Secondary battery: status unavailable";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRefreshingBattery))]
    private bool _isRefreshingBattery;

    /// <summary>Inverse of IsRefreshingBattery for XAML MultiBinding.</summary>
    public bool IsNotRefreshingBattery => !IsRefreshingBattery;

    [ObservableProperty]
    private string? _batteryStatusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotTestingConnection))]
    private bool _isTestingConnection;

    /// <summary>Inverse of IsTestingConnection for XAML MultiBinding.</summary>
    public bool IsNotTestingConnection => !IsTestingConnection;

    /// <summary>
    /// Device setting: turn the status LED off during therapy sessions (THERAPY_LED_OFF).
    /// </summary>
    [ObservableProperty]
    private bool _therapyLedOff;

    /// <summary>
    /// True once the therapy LED setting has been read from the connected device;
    /// the toggle is hidden until then.
    /// </summary>
    [ObservableProperty]
    private bool _therapyLedLoaded;

    /// <summary>
    /// True while a therapy-LED read or write is in flight; disables the switch so
    /// toggles can't overlap (a stale failure would otherwise desync the UI).
    /// </summary>
    [ObservableProperty]
    private bool _therapyLedBusy;

    /// <summary>
    /// True from a profile-applying reboot until the gloves reconnect (or reconnection
    /// fails). The Devices page shows an "Applying Settings" card instead of the scan
    /// UI during this window.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDisconnectedView))]
    private bool _isApplyingSettings;

    /// <summary>
    /// Gates the scan/connect UI: shown only when disconnected AND not mid-apply.
    /// </summary>
    public bool ShowDisconnectedView => !ConnectionInfo.IsConnected && !IsApplyingSettings;

    /// <summary>Backstop for the apply-reconnect watch, in case no terminal reconnection event arrives.</summary>
    private const int ApplyReconnectBackstopMs = 120_000;

    private CancellationTokenSource? _applyWatchCts;

    public DeviceSettingsViewModel(
        IGloveControlService gloveControlService,
        IBluetoothService bluetoothService,
        IDataStorageService storageService,
        IConnectionStateService connectionStateService,
        IReconnectionService reconnectionService)
    {
        _gloveControlService = gloveControlService;
        _bluetoothService = bluetoothService;
        _storageService = storageService;
        ConnectionInfo = connectionStateService;
        _reconnectionService = reconnectionService;

        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;
        _gloveControlService.SessionStateChanged += OnSessionStateChanged;
        _gloveControlService.DeviceProfileChanged += OnDeviceProfileChanged;
        _reconnectionService.ReconnectionStateChanged += OnReconnectionStateChanged;

        LoadProfilesAsync().SafeFireAndForget("[DEVICESETTINGS]");
    }

    /// <summary>
    /// Called from DeviceListPage.OnAppearing. Refreshes device-side state when connected.
    /// </summary>
    public void OnPageAppearing()
    {
        IsSessionActive = _gloveControlService.CurrentSessionStatus.IsActive;

        if (!ConnectionInfo.IsConnected)
        {
            TherapyLedLoaded = false;
            return;
        }

        LoadDeviceSettingsAsync().SafeFireAndForget("[DEVICESETTINGS]");
        RefreshBatteryAsync().SafeFireAndForget("[DEVICESETTINGS]");

        // The service normally fetches INFO on connect; this is just a fallback for
        // the rare case where both post-connect attempts failed.
        if (_gloveControlService.DeviceProfileId <= 0)
        {
            _gloveControlService.GetDeviceInfoAsync().SafeFireAndForget("[DEVICESETTINGS]");
        }
    }

    /// <summary>
    /// Selects a profile locally. Nothing is written to the gloves until the user
    /// presses Apply Settings.
    /// </summary>
    [RelayCommand]
    private async Task SelectProfileAsync(ProfileItemViewModel profileItem)
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

        // Save selection preference for next app launch
        await _storageService.SaveLastProfileAsync(profileItem.Profile.ProfileId);
    }

    /// <summary>
    /// Writes every pending device setting (therapy LED, then profile) to the gloves
    /// in one go. The LED is written first because a profile change reboots the gloves.
    /// </summary>
    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        if (!ConnectionInfo.IsConnected || !HasPendingChanges)
            return;

        if (IsSessionActive)
        {
            await Shell.Current.DisplayAlert(
                "Session Active",
                "Stop the current session before changing device settings.",
                "OK");
            return;
        }

        var applyProfile = IsProfileDirty;
        if (applyProfile)
        {
            var confirm = await Shell.Current.DisplayAlert(
                "Apply Settings?",
                $"Switching to \"{SelectedProfile!.Name}\" will restart your gloves. " +
                "They will reconnect automatically in a few moments.",
                "Apply",
                "Cancel");
            if (!confirm)
                return;
        }

        IsBusy = true;
        try
        {
            if (IsLedDirty)
            {
                await _gloveControlService.SetTherapyLedOffAsync(TherapyLedOff);
                _deviceTherapyLedOff = TherapyLedOff;
            }

            if (applyProfile)
            {
                await _gloveControlService.LoadProfileAsync(SelectedProfile!.ProfileId);
                ProfileStatusMessage = ProfileRebootWarning;
                BeginApplyReconnectWatch();
            }

            SemanticScreenReader.Announce("Device settings applied");
        }
        catch (BlueBuzzahCommandException ex)
        {
            var (title, message) = ErrorMessageHelper.GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(title, message, "OK");
            await ResyncTherapyLedAsync();
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert(GetErrorTitle(ex), GetErrorMessage(ex), "OK");
            await ResyncTherapyLedAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasPendingChanges));
        }
    }

    /// <summary>
    /// After a failed apply, re-read the LED state so the toggle shows what's actually
    /// on the device; if even the read fails, hide the toggle.
    /// </summary>
    private async Task ResyncTherapyLedAsync()
    {
        try
        {
            _suppressTherapyLedWrite = true;
            TherapyLedOff = await _gloveControlService.GetTherapyLedOffAsync();
            _deviceTherapyLedOff = TherapyLedOff;
        }
        catch
        {
            TherapyLedLoaded = false;
        }
        finally
        {
            _suppressTherapyLedWrite = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToCalibrationAsync()
    {
        await Shell.Current.GoToAsync(Routes.Calibration);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (!ConnectionInfo.IsConnected)
            return;

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
            return;

        IsRefreshingBattery = true;

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

            // Primary always has a battery; a null secondary may just mean no
            // secondary glove (firmware reports both as BATS:0.00), so only
            // flag the primary.
            BatteryStatusMessage = primaryVoltage is null
                ? "Battery status unavailable — try refreshing"
                : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Battery refresh error: {ex.Message}");
            BatteryStatusMessage = "Battery status unavailable";
        }
        finally
        {
            IsRefreshingBattery = false;
        }
    }

    /// <summary>
    /// Reads device-side settings (therapy LED) from the connected glove.
    /// </summary>
    [RelayCommand]
    private async Task LoadDeviceSettingsAsync()
    {
        if (TherapyLedBusy)
            return;

        if (!ConnectionInfo.IsConnected)
        {
            TherapyLedLoaded = false;
            return;
        }

        TherapyLedBusy = true;
        try
        {
            _suppressTherapyLedWrite = true;
            TherapyLedOff = await _gloveControlService.GetTherapyLedOffAsync();
            _deviceTherapyLedOff = TherapyLedOff;
            TherapyLedLoaded = true;
        }
        catch
        {
            // Older firmware may not support THERAPY_LED_OFF; just hide the toggle.
            TherapyLedLoaded = false;
        }
        finally
        {
            _suppressTherapyLedWrite = false;
            TherapyLedBusy = false;
            OnPropertyChanged(nameof(HasPendingChanges));
        }
    }

    partial void OnTherapyLedOffChanged(bool value)
    {
        // No device write here — changes are batched behind Apply Settings.
        if (!_suppressTherapyLedWrite)
        {
            OnPropertyChanged(nameof(HasPendingChanges));
        }
    }

    partial void OnSelectedProfileChanged(TherapyProfile? value)
    {
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    private async Task LoadProfilesAsync()
    {
        var profiles = TherapyProfile.GetPresetProfiles();

        AvailableProfiles.Clear();
        foreach (var profile in profiles)
        {
            AvailableProfiles.Add(new ProfileItemViewModel(profile));
        }

        // Load last used profile or default to Noisy (profile 2)
        int lastProfileId;
        try
        {
            lastProfileId = await _storageService.GetLastProfileAsync();
        }
        catch
        {
            lastProfileId = 2;
        }

        var selectedItem = AvailableProfiles.FirstOrDefault(p => p.ProfileId == lastProfileId)
                        ?? AvailableProfiles.FirstOrDefault(p => p.ProfileId == 2)
                        ?? AvailableProfiles.FirstOrDefault();

        if (selectedItem != null)
        {
            selectedItem.IsSelected = true;
            SelectedProfile = selectedItem.Profile;
        }
    }

    /// <summary>
    /// Syncs card selection to the profile the device reports. GloveControlService
    /// fetches INFO once per (re)connect and raises this for every INFO response.
    /// </summary>
    private void OnDeviceProfileChanged(object? sender, int profileId)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Clear a stuck "restarting" banner from a profile-change reboot — the
            // INFO response means the gloves are back.
            if (ProfileStatusMessage == ProfileRebootWarning)
            {
                ProfileStatusMessage = null;
            }

            var match = AvailableProfiles.FirstOrDefault(p => p.Profile?.ProfileId == profileId);
            if (match != null)
            {
                foreach (var item in AvailableProfiles)
                {
                    item.IsSelected = item == match;
                }
                SelectedProfile = match.Profile;
            }

            // The profile baseline changed even if the local selection didn't.
            OnPropertyChanged(nameof(HasPendingChanges));
        });
    }

    /// <summary>
    /// Shows the "Applying Settings" card in place of the scan UI while the gloves
    /// reboot and auto-reconnect. Ends on reconnect, on a terminal reconnection
    /// event, or via a backstop timeout.
    /// </summary>
    private void BeginApplyReconnectWatch()
    {
        IsApplyingSettings = true;
        _applyWatchCts?.Cancel();
        _applyWatchCts?.Dispose();
        _applyWatchCts = new CancellationTokenSource();
        var ct = _applyWatchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ApplyReconnectBackstopMs, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            await MainThread.InvokeOnMainThreadAsync(() => EndApplyReconnectWatch(succeeded: false));
        });
    }

    /// <summary>Must be called on the main thread.</summary>
    private void EndApplyReconnectWatch(bool succeeded)
    {
        if (!IsApplyingSettings)
            return;

        IsApplyingSettings = false;
        _applyWatchCts?.Cancel();

        if (succeeded)
        {
            SemanticScreenReader.Announce("Gloves reconnected. Settings applied.");
        }
        else
        {
            ProfileStatusMessage = null;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Shell.Current.DisplayAlert(
                    "Reconnect Failed",
                    "The settings were sent, but the gloves haven't reconnected. " +
                    "Make sure they're powered on, then connect again below.",
                    "OK");
            });
        }
    }

    private void OnReconnectionStateChanged(object? sender, ReconnectionStateEventArgs e)
    {
        if (e.State is ReconnectionState.Failed or ReconnectionState.Cancelled)
        {
            MainThread.BeginInvokeOnMainThread(() => EndApplyReconnectWatch(succeeded: false));
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (state == ConnectionState.Connected)
            {
                EndApplyReconnectWatch(succeeded: true);
                LoadDeviceSettingsAsync().SafeFireAndForget("[DEVICESETTINGS]");
                RefreshBatteryAsync().SafeFireAndForget("[DEVICESETTINGS]");
            }
            else
            {
                TherapyLedLoaded = false;
            }

            OnPropertyChanged(nameof(ShowDisconnectedView));
        });
    }

    private void OnSessionStateChanged(object? sender, SessionStatus status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            IsSessionActive = status.IsActive;
        });
    }

    /// <summary>
    /// Unsubscribes from service events to prevent memory leaks.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
            _gloveControlService.SessionStateChanged -= OnSessionStateChanged;
            _gloveControlService.DeviceProfileChanged -= OnDeviceProfileChanged;
            _reconnectionService.ReconnectionStateChanged -= OnReconnectionStateChanged;
            _applyWatchCts?.Cancel();
            _applyWatchCts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
