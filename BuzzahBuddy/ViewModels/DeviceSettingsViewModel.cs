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

    private const string ProfileRebootWarning = "Gloves are restarting to apply the new profile…";

    /// <summary>Suppresses the device write in OnTherapyLedOffChanged while syncing from the device.</summary>
    private bool _suppressTherapyLedWrite;

    /// <summary>
    /// Centralized connection state service exposed for XAML binding.
    /// </summary>
    public IConnectionStateService ConnectionInfo { get; }

    [ObservableProperty]
    private ObservableCollection<ProfileItemViewModel> _availableProfiles = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    private TherapyProfile? _selectedProfile;

    /// <summary>True when a profile is selected; shows the Adjust Settings button.</summary>
    public bool HasSelectedProfile => SelectedProfile != null;

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

    public DeviceSettingsViewModel(
        IGloveControlService gloveControlService,
        IBluetoothService bluetoothService,
        IDataStorageService storageService,
        IConnectionStateService connectionStateService)
    {
        _gloveControlService = gloveControlService;
        _bluetoothService = bluetoothService;
        _storageService = storageService;
        ConnectionInfo = connectionStateService;

        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;
        _gloveControlService.SessionStateChanged += OnSessionStateChanged;

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
        SyncSelectedProfileFromDeviceAsync().SafeFireAndForget("[DEVICESETTINGS]");
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
                ProfileStatusMessage = ProfileRebootWarning;
            }
            catch (BlueBuzzahCommandException ex)
            {
                // Device rejected the profile — keep the previous selection.
                var (title, message) = ErrorMessageHelper.GetFriendlyError(ex.Message);
                await Shell.Current.DisplayAlert(title, message, "OK");
                return;
            }
            catch (Exception ex)
            {
                await Shell.Current.DisplayAlert(GetErrorTitle(ex), GetErrorMessage(ex), "OK");
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

        // Save selection preference for next app launch
        await _storageService.SaveLastProfileAsync(profileItem.Profile.ProfileId);
    }

    [RelayCommand]
    private async Task NavigateToProfileSettingsAsync()
    {
        await Shell.Current.GoToAsync(Routes.ProfileSettings);
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
        }
    }

    partial void OnTherapyLedOffChanged(bool value)
    {
        if (_suppressTherapyLedWrite)
            return;

        // async via discard: Switch toggles aren't awaitable. TherapyLedBusy disables
        // the switch until the write settles, so writes can't overlap.
        _ = ApplyTherapyLedOffAsync(value);
    }

    private async Task ApplyTherapyLedOffAsync(bool value)
    {
        TherapyLedBusy = true;
        try
        {
            await _gloveControlService.SetTherapyLedOffAsync(value);
        }
        catch (Exception ex)
        {
            // Re-sync from the device rather than blind-reverting, so the switch
            // shows the device's actual state; if even the read fails, hide it.
            try
            {
                _suppressTherapyLedWrite = true;
                TherapyLedOff = await _gloveControlService.GetTherapyLedOffAsync();
            }
            catch
            {
                TherapyLedLoaded = false;
            }
            finally
            {
                _suppressTherapyLedWrite = false;
            }

            await Shell.Current.DisplayAlert(GetErrorTitle(ex), GetErrorMessage(ex), "OK");
        }
        finally
        {
            TherapyLedBusy = false;
        }
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
    /// Queries the device for its currently loaded profile and syncs local selection state
    /// to match. Called on every (re)connect so the UI never disagrees with the device.
    /// </summary>
    private async Task SyncSelectedProfileFromDeviceAsync()
    {
        try
        {
            await _gloveControlService.GetDeviceInfoAsync();

            // Clear a stuck "restarting" banner from a profile-change reboot on any
            // successful INFO fetch.
            if (ProfileStatusMessage == ProfileRebootWarning)
            {
                ProfileStatusMessage = null;
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
            System.Diagnostics.Debug.WriteLine($"[DEVICESETTINGS] Profile sync failed: {ex.Message}");
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (state == ConnectionState.Connected)
            {
                LoadDeviceSettingsAsync().SafeFireAndForget("[DEVICESETTINGS]");
                RefreshBatteryAsync().SafeFireAndForget("[DEVICESETTINGS]");
                SyncSelectedProfileFromDeviceAsync().SafeFireAndForget("[DEVICESETTINGS]");
            }
            else
            {
                TherapyLedLoaded = false;
            }
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
        }
        base.Dispose(disposing);
    }
}
