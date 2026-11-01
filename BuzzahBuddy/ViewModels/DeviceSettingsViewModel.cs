using BuzzahBuddy.Helpers;
using BuzzahBuddy.Messages;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Dialogs;
using BuzzahBuddy.Services.Firmware;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using static BuzzahBuddy.Services.Glove.ErrorMessageHelper;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the device-settings section of the Device page.
/// Owns settings that live on the connected glove: therapy profile, therapy LED,
/// and battery status. Changes are staged locally and
/// written to the glove in one batch when the user presses Apply Settings;
/// device settings are unavailable when disconnected.
/// </summary>
public partial class DeviceSettingsViewModel : BaseViewModel
{
    private readonly IGloveControlService _gloveControlService;
    private readonly IBluetoothService _bluetoothService;
    private readonly IDataStorageService _storageService;
    private readonly IReconnectionService _reconnectionService;
    private readonly IConfirmationService _confirmation;

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

    partial void OnIsSessionActiveChanged(bool value) => ResolveBanner();

    /// <summary>Inverse of IsSessionActive for XAML binding.</summary>
    public bool IsSessionInactive => !IsSessionActive;

    /// <summary>
    /// True when the selection on this page differs from what's on the gloves. Apply
    /// sends just these; with none, it applies everything again.
    /// </summary>
    public bool HasPendingChanges => IsLedDirty || IsProfileDirty || IsCustomDirty;

    private bool IsLedDirty => TherapyLedLoaded && TherapyLedOff != _deviceTherapyLedOff;

    private bool IsProfileDirty =>
        ConnectionInfo.IsConnected
        && _gloveControlService.DeviceProfileId > 0
        && SelectedProfile != null
        && SelectedProfile.ProfileId != _gloveControlService.DeviceProfileId;

    // ponytail: in-memory flag, so edits saved before an app restart don't light Apply
    // while the gloves already run Custom; compare against a PROFILE_GET read if that matters.
    /// <summary>True after the user saves Custom settings that haven't reached the gloves yet.</summary>
    private bool _customEdited;

    /// <summary>
    /// True from the user's own profile tap until the gloves report that profile. Until then the
    /// selection is the page's fallback (saved profile, or Noisy), which the glove's report replaces.
    /// </summary>
    private bool _profilePicked;

    /// <summary>Saved Custom settings to send once the gloves are back from loading Custom.</summary>
    private TherapyProfile? _customToSend;

    private bool IsCustomDirty =>
        _customEdited
        && ConnectionInfo.IsConnected
        && SelectedProfile?.IsCustom == true
        && _gloveControlService.DeviceProfileId == TherapyProfile.CustomProfileId;

    // Null voltage = no reading available (missing key or firmware 0.00 sentinel)
    /// <summary>The primary glove's firmware version, shown above the glove cards.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareDescription))]
    private string? _firmwareVersion;

    /// <summary>The primary's firmware is older than the newest release; shows the "Outdated" badge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareDescription))]
    private bool _firmwareOutdated;

    /// <summary>The firmware line as a screen reader hears it: its container hides the badge.</summary>
    public string FirmwareDescription =>
        FirmwareOutdated ? $"Firmware version {FirmwareVersion}, outdated" : $"Firmware version {FirmwareVersion}";

    private string? _latestFirmwareTag;

    // Each new link's version is checked once; repeat INFOs carry the same value.
    partial void OnFirmwareVersionChanged(string? value)
    {
        FirmwareOutdated = false;
        ResolveBanner();
        CheckFirmwareAsync(value).SafeFireAndForget("[DEVICESETTINGS]");
    }

    [ObservableProperty]
    private double? _batteryPrimaryVoltage;

    [ObservableProperty]
    private double? _batterySecondaryVoltage;

    [ObservableProperty]
    private GloveStatusInfo _primaryGlove = new(GloveRole.Primary, GloveLinkStatus.Disconnected, null);

    [ObservableProperty]
    private GloveStatusInfo _secondaryGlove = new(GloveRole.Secondary, GloveLinkStatus.Disconnected, null);

    // INFO's SECONDARY key; null on older firmware or before this link's first INFO
    private bool? _secondaryLinked;

    // The connected view's one banner (Banner README).
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

    private (StatusTone Tone, string Title, string Message)? _resultBanner;

    /// <summary>
    /// True after an apply whose gloves never reconnected; the Gloves scan view shows a
    /// warning banner for it until the next connect.
    /// </summary>
    [ObservableProperty]
    private bool _applyReconnectFailed;

    /// <summary>True when the apply wait has run past the backstop (plan choice PC22).</summary>
    [ObservableProperty]
    private bool _isWaitingLong;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotRefreshingDeviceInfo))]
    private bool _isRefreshingDeviceInfo;

    /// <summary>Inverse of IsRefreshingDeviceInfo for XAML MultiBinding.</summary>
    public bool IsNotRefreshingDeviceInfo => !IsRefreshingDeviceInfo;

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

    // OnPageAppearing accepts readings this fresh from the service instead of
    // querying again: popping the apply modal re-shows this page right as the
    // reconnect sync runs, in the window where the primary is busiest re-syncing
    // with the secondary. The service also joins a query already in flight.
    private static readonly TimeSpan DeviceReadingFreshness = TimeSpan.FromSeconds(30);

    public DeviceSettingsViewModel(
        IGloveControlService gloveControlService,
        IBluetoothService bluetoothService,
        IDataStorageService storageService,
        IConnectionStateService connectionStateService,
        IReconnectionService reconnectionService,
        IConfirmationService confirmation)
    {
        _confirmation = confirmation;
        _gloveControlService = gloveControlService;
        _bluetoothService = bluetoothService;
        _storageService = storageService;
        ConnectionInfo = connectionStateService;
        _reconnectionService = reconnectionService;

        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;
        _gloveControlService.SessionStateChanged += OnSessionStateChanged;
        _gloveControlService.DeviceProfileChanged += OnDeviceProfileChanged;
        _gloveControlService.DeviceInfoReceived += OnDeviceInfoReceived;
        _gloveControlService.TherapyLedOffReceived += OnTherapyLedOffReceived;
        _reconnectionService.ReconnectionStateChanged += OnReconnectionStateChanged;
        WeakReferenceMessenger.Default.Register<DeviceSettingsViewModel, CustomProfileSavedMessage>(this, (vm, _) =>
        {
            vm._customEdited = true;
            vm.NotifyPendingChanged();
        });

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

        // The link comes from the connection, not INFO: a page built after the connect still
        // shows the primary connected if the INFO below fails.
        UpdateGloves();
        LoadTherapyLedAsync(DeviceReadingFreshness).SafeFireAndForget("[DEVICESETTINGS]");

        // Re-fetch INFO on visits; it also refreshes battery (OnDeviceInfoReceived), so
        // no separate BATTERY. DeviceProfileId persists across connections, so a
        // "still set" value can be stale from the previous connection if the
        // post-connect fetch failed — the service holds no reading then, so this
        // queries. One cheap round-trip keeps this page truthful. A cached reading raises no
        // event, and this page may not have existed for the post-connect one (launch
        // auto-connect), so apply what comes back.
        ApplyRecentDeviceInfoAsync().SafeFireAndForget("[DEVICESETTINGS]");
    }

    private async Task ApplyRecentDeviceInfoAsync() =>
        OnDeviceInfoReceived(this, await _gloveControlService.GetDeviceInfoAsync(DeviceReadingFreshness));

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
        _profilePicked = true;

        // Ensure PropertyChanged fires on UI thread for nested bindings
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SelectedProfile = profileItem.Profile;
        });

        // Save selection preference for next app launch
        await _storageService.SaveLastProfileAsync(profileItem.Profile.ProfileId);
    }

    /// <summary>
    /// Opens the Custom profile's editor. It saves on the phone, so it needs no glove,
    /// selection or stopped session.
    /// </summary>
    [RelayCommand]
    private Task EditCustomParametersAsync() => Shell.Current.GoToAsync(Routes.ProfileSettings);

    /// <summary>
    /// Writes every pending device setting (therapy LED, then profile, then the saved
    /// Custom settings) to the gloves in one go. The LED is written first because a
    /// profile change reboots the gloves; Custom settings go once they're back. With
    /// nothing pending it applies all of them again, a troubleshooting aid.
    /// </summary>
    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        // Synchronous re-entrancy guard: a tremor double-tap can fire this twice
        // before IsBusy is set below (the button's IsEnabled binding is async).
        // Without this, two overlapping applies could stack two blocking modals.
        if (IsBusy || IsApplyingSettings)
            return;

        if (!ConnectionInfo.IsConnected || SelectedProfile is not { } profile)
            return;

        if (IsSessionActive)
        {
            await Shell.Current.DisplayAlert(
                "Stop Your Session First",
                "Settings can't change while a session runs.",
                "OK");
            return;
        }

        var reapply = !HasPendingChanges;
        var applyProfile = IsProfileDirty || reapply;
        var writeLed = IsLedDirty || (reapply && TherapyLedLoaded);
        var sendCustomNow = IsCustomDirty;
        // The firmware takes Custom edits only while Custom is loaded, so after a load
        // they wait for the reboot (OnDeviceProfileChanged).
        var savedCustom = profile.IsCustom ? await _storageService.GetCustomProfileAsync() : null;
        if (applyProfile)
        {
            var confirm = await _confirmation.ConfirmAsync(new ConfirmRequest(
                "Restart Gloves to Apply?",
                reapply
                    ? $"Applying \u201c{profile.Name}\u201d again restarts both gloves. They usually reconnect on their own in under a minute."
                    : $"Switching to \u201c{profile.Name}\u201d restarts both gloves. They usually reconnect on their own in under a minute.",
                "Apply and Restart"));
            if (!confirm)
                return;
        }

        IsBusy = true;
        _resultBanner = null;
        ResolveBanner();

        // Show the blocking modal BEFORE any write when a profile change is part of
        // the batch: LoadProfileAsync sends PROFILE_LOAD, which reboots the gloves
        // during its await — so pushing the modal afterward meant the user saw (and
        // heard) the reboot before the indicator ever appeared. Awaited so it's up
        // before the reboot-triggering command goes out.
        if (applyProfile)
        {
            ProfileStatusMessage = ProfileRebootWarning;
            _customToSend = savedCustom;
            await BeginApplyReconnectWatchAsync();
        }

        try
        {
            if (writeLed)
            {
                await _gloveControlService.SetTherapyLedOffAsync(TherapyLedOff);
                _deviceTherapyLedOff = TherapyLedOff;
            }

            if (applyProfile)
            {
                // Returns normally when the reboot is expected; the running watch
                // ends when the gloves reconnect. Only throws on a genuine, still-
                // connected failure (handled below — no reboot happened).
                await _gloveControlService.LoadProfileAsync(profile.ProfileId);
            }

            // A profile change confirms after the gloves reconnect (EndApplyReconnectWatch);
            // LED and Custom-settings changes are already on the gloves.
            if (!applyProfile)
            {
                if (sendCustomNow && savedCustom is not null && !await SendCustomAsync(savedCustom))
                    return;
                ConfirmSaved();
            }
        }
        catch (BlueBuzzahCommandException ex)
        {
            AbortApplyReconnectWatch();
            var error = ErrorMessageHelper.GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
            await ResyncTherapyLedAsync();
        }
        catch (Exception ex)
        {
            AbortApplyReconnectWatch();
            var error = FromException(ex);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
            await ResyncTherapyLedAsync();
        }
        finally
        {
            IsBusy = false;
            NotifyPendingChanged();
        }
    }

    /// <summary>
    /// Sends the saved Custom settings, with the outcome in the banner. On failure Apply
    /// stays lit so the user can try again; false if they didn't all go.
    /// </summary>
    private async Task<bool> SendCustomAsync(TherapyProfile saved)
    {
        try
        {
            await _gloveControlService.SendSavedCustomProfileAsync(saved);
            _customEdited = false;
            _resultBanner = _gloveControlService.PersistsCustomProfile ? null
                : (StatusTone.Info, "Saved until the Gloves Restart",
                    "This firmware doesn't store Custom settings. Update it with the BlueBuzzah Updater to keep them.");
            return true;
        }
        catch (Exception ex)
        {
            _customEdited = true;
            _resultBanner = (StatusTone.Danger, "Couldn't Send Your Custom Settings", FromException(ex).Message);
            return false;
        }
        finally
        {
            ResolveBanner();
            NotifyPendingChanged();
        }
    }

    private async Task SendCustomThenEndWatchAsync(TherapyProfile saved)
    {
        var sent = await SendCustomAsync(saved);
        EndApplyReconnectWatch(succeeded: true, confirm: sent);
    }

    /// <summary>
    /// After a failed apply, re-read the LED state so the toggle shows what's actually
    /// on the device; if even the read fails, hide the toggle.
    /// </summary>
    private async Task ResyncTherapyLedAsync()
    {
        try
        {
            ApplyTherapyLedState(await _gloveControlService.GetTherapyLedOffAsync());
        }
        catch
        {
            TherapyLedLoaded = false;
        }
    }

    [RelayCommand]
    private async Task NavigateToCalibrationAsync()
    {
        await Shell.Current.GoToAsync(Routes.Calibration);
    }

    /// <summary>
    /// Asks for a fresh INFO: firmware version and both batteries. Its reply lands in
    /// OnDeviceInfoReceived like the post-connect one.
    /// </summary>
    [RelayCommand]
    private async Task RefreshDeviceInfoAsync()
    {
        if (!ConnectionInfo.IsConnected)
            return;

        IsRefreshingDeviceInfo = true;

        try
        {
            await _gloveControlService.GetDeviceInfoAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Device info refresh error: {ex.Message}");
        }
        finally
        {
            IsRefreshingDeviceInfo = false;
        }
    }

    private void ApplyBatteryReading(double? primaryVoltage, double? secondaryVoltage)
    {
        BatteryPrimaryVoltage = primaryVoltage;
        BatterySecondaryVoltage = secondaryVoltage;
        UpdateGloves();
    }

    /// <summary>
    /// GloveStatus for both gloves, with raw volts in code-s (design system
    /// Principles #4). The secondary's state comes from the primary's INFO
    /// (<see cref="GloveStatusText.SecondaryLink"/>).
    /// </summary>
    private void UpdateGloves()
    {
        var link = ConnectionInfo.IsConnected ? GloveLinkStatus.Connected : GloveLinkStatus.Disconnected;
        int? Percent(double? v) => v is { } volts ? BatteryReading.ToPercentage(volts) : null;
        if (link != GloveLinkStatus.Connected)
            _secondaryLinked = null;
        PrimaryGlove = new GloveStatusInfo(GloveRole.Primary, link, Percent(BatteryPrimaryVoltage),
            BatteryReading.FormatVolts(BatteryPrimaryVoltage));
        var secondaryLink = GloveStatusText.SecondaryLink(link, _secondaryLinked, BatterySecondaryVoltage);
        SecondaryGlove = new GloveStatusInfo(GloveRole.Secondary, secondaryLink, Percent(BatterySecondaryVoltage),
            Detail: BatteryReading.FormatVolts(BatterySecondaryVoltage));
    }

    /// <summary>Session-locked warning beats the last action's result, which beats outdated firmware.</summary>
    private void ResolveBanner()
    {
        (StatusTone Tone, string Title, string Message)? banner =
            IsSessionActive && ConnectionInfo.IsConnected
                ? (StatusTone.Warning, "Stop Your Session to Change Settings", "Glove settings are locked while a session runs.")
                : _resultBanner;
        var showUpdate = banner is null && FirmwareOutdated;
        if (showUpdate)
            banner = (StatusTone.Warning, "Firmware Update Available",
                $"Your primary glove has v{FirmwareVersion} and the latest is {_latestFirmwareTag}. Update both gloves with the BlueBuzzah Updater on a computer.");
        BannerVisible = banner is not null;
        BannerActionText = showUpdate ? "How to Update" : null;
        if (banner is { } b)
            (BannerTone, BannerTitle, BannerMessage) = b;
    }

    /// <summary>Flags firmware older than the newest release. Demo gloves never are.</summary>
    private async Task CheckFirmwareAsync(string? glove)
    {
        if (glove is null || Preferences.Default.Get(SwitchableBluetoothService.DemoModeKey, false))
            return;
        var latest = await LatestFirmware.GetTagAsync();
        if (glove != FirmwareVersion)
            return; // the link changed while we were asking
        _latestFirmwareTag = latest;
        FirmwareOutdated = FirmwareRelease.IsOutdated(glove, latest);
        ResolveBanner();
    }

    /// <summary>
    /// Firmware isn't flashed from the phone, so the sheet says how to use the desktop
    /// Updater and offers to send its download link to a computer.
    /// </summary>
    [RelayCommand]
    private async Task ShowFirmwareUpdateAsync()
    {
        var share = await _confirmation.ConfirmAsync(new ConfirmRequest(
            "Update Your Glove Firmware",
            $"The latest firmware is {_latestFirmwareTag}. Your primary glove has v{FirmwareVersion}.\n\n"
            + "1. On a computer, download the BlueBuzzah Updater from BlueBuzzah.com.\n"
            + "2. Connect your primary glove with its USB cable and follow the steps in the Updater.\n"
            + "3. Do the same for your secondary glove.",
            ConfirmText: "Share Updater Link",
            CancelText: "Done"));
        if (share)
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "BlueBuzzah Updater",
                Uri = LatestFirmware.UpdaterUrl,
            });
    }

    private void NotifyPendingChanged() => OnPropertyChanged(nameof(HasPendingChanges));

    // No badge (owner's choice); screen readers still hear it.
    private static void ConfirmSaved() => SemanticScreenReader.Announce("Settings saved to both gloves");

    /// <summary>
    /// INFO carries both batteries; the service sends one right after every
    /// (re)connect, so this replaces a separate on-connect BATTERY command.
    /// </summary>
    private void OnDeviceInfoReceived(object? sender, GloveDeviceInfo info)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            FirmwareVersion = info.FirmwareVersion is { Length: > 0 } and not "0.0.0" ? info.FirmwareVersion : null;
            _secondaryLinked = info.SecondaryLinked;
            ApplyBatteryReading(info.BatteryPrimaryVoltage, info.BatterySecondaryVoltage);
        });
    }

    /// <summary>
    /// The service's post-connect THERAPY_LED_OFF query reports here. If that query
    /// fails (older firmware), no event arrives and the toggle stays hidden.
    /// </summary>
    private void OnTherapyLedOffReceived(object? sender, bool ledOff)
    {
        MainThread.BeginInvokeOnMainThread(() => ApplyTherapyLedState(ledOff));
    }

    /// <summary>
    /// Shows the device's therapy LED setting. The one place the toggle takes a device
    /// value: the post-connect event, page loads and the post-failure resync all land
    /// here (a load can be served from the service's cache, which raises no event).
    /// </summary>
    private void ApplyTherapyLedState(bool ledOff)
    {
        _suppressTherapyLedWrite = true;
        TherapyLedOff = ledOff;
        _deviceTherapyLedOff = ledOff;
        TherapyLedLoaded = true;
        _suppressTherapyLedWrite = false;
        NotifyPendingChanged();
    }

    /// <summary>
    /// Reads the therapy LED setting from the connected glove, accepting a reading the
    /// service already has if it is at most <paramref name="maxAge"/> old.
    /// </summary>
    private async Task LoadTherapyLedAsync(TimeSpan? maxAge)
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
            ApplyTherapyLedState(await _gloveControlService.GetTherapyLedOffAsync(maxAge));
        }
        catch
        {
            // Older firmware may not support THERAPY_LED_OFF; just hide the toggle.
            TherapyLedLoaded = false;
        }
        finally
        {
            TherapyLedBusy = false;
        }
    }

    partial void OnTherapyLedOffChanged(bool value)
    {
        // No device write here — changes are batched behind Apply Settings.
        if (!_suppressTherapyLedWrite)
        {
            NotifyPendingChanged();
        }
    }

    partial void OnSelectedProfileChanged(TherapyProfile? value)
    {
        NotifyPendingChanged();
    }

    private async Task LoadProfilesAsync()
    {
        var profiles = TherapyProfile.GetPresetProfiles();

        AvailableProfiles.Clear();
        foreach (var profile in profiles)
        {
            AvailableProfiles.Add(new ProfileItemViewModel(profile));
        }

        // The glove's own profile once it has reported one (this page can be built after the
        // connect, e.g. launch auto-connect, when DeviceProfileChanged has already fired);
        // otherwise the last used profile, or Noisy (profile 2).
        int lastProfileId = _gloveControlService.DeviceProfileId;
        if (lastProfileId <= 0)
        {
            try
            {
                lastProfileId = await _storageService.GetLastProfileAsync();
            }
            catch
            {
                lastProfileId = 2;
            }
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

            // Back from loading Custom: send the saved settings while the modal stays up.
            if (IsApplyingSettings && _customToSend is { } saved && profileId == TherapyProfile.CustomProfileId)
            {
                _customToSend = null;
                SendCustomThenEndWatchAsync(saved).SafeFireAndForget("[DEVICESETTINGS]");
            }

            // Don't clobber an unsaved local selection. If the user picked a profile
            // and hasn't pressed Apply, a reconnect's INFO would otherwise silently
            // revert their choice. IsProfileDirty compares the selection to the
            // device's just-updated DeviceProfileId, so this only skips the sync
            // when the user has a genuinely different pending pick (Apply stays lit).
            // A fallback selection (page built before INFO, e.g. during auto-connect)
            // isn't a pick, so it syncs.
            if (!IsProfileDirty || !_profilePicked)
            {
                var match = AvailableProfiles.FirstOrDefault(p => p.Profile?.ProfileId == profileId);
                if (match != null)
                {
                    foreach (var item in AvailableProfiles)
                    {
                        item.IsSelected = item == match;
                    }
                    SelectedProfile = match.Profile;
                }
            }

            if (SelectedProfile?.ProfileId == profileId)
                _profilePicked = false;

            // The profile baseline changed even if the local selection didn't.
            NotifyPendingChanged();
        });
    }

    /// <summary>
    /// Shows the blocking "Applying Settings" modal and starts the reconnect watch.
    /// Awaited so the modal is on screen before the caller sends the reboot-triggering
    /// PROFILE_LOAD. Ends on reconnect, a terminal reconnection event, or a backstop.
    /// </summary>
    private async Task BeginApplyReconnectWatchAsync()
    {
        // Idempotent: never stack a second modal / watch.
        if (IsApplyingSettings)
            return;

        IsApplyingSettings = true;

        // Blocking modal: covers the tab bar so the user can't wander off into
        // half-alive screens while the gloves reboot and reconnect.
        try
        {
            await Shell.Current.Navigation.PushModalAsync(new Views.ApplyingSettingsPage(this));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DEVICESETTINGS] Apply modal push failed: {ex.Message}");

            // Without the modal, keeping IsApplyingSettings set would hide BOTH the
            // connected view and the scan view once the reboot drops the link,
            // leaving a blank Device page until the backstop fired. Fall back to the
            // normal disconnected UI instead: the reconnection service's banner
            // covers the reboot, and the profile sync on reconnect clears the
            // reboot notice.
            IsApplyingSettings = false;
            return;
        }

        _applyWatchCts?.Cancel();
        _applyWatchCts?.Dispose();
        _applyWatchCts = new CancellationTokenSource();
        var ct = _applyWatchCts.Token;

        StartWaitBackstop(ct);
    }

    /// <summary>
    /// After the backstop the wait page asks instead of giving up silently: Keep
    /// waiting restarts it, Stop waiting ends the watch (accessibility.md: no time limits).
    /// </summary>
    private void StartWaitBackstop(CancellationToken ct)
    {
        IsWaitingLong = false;
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
            await MainThread.InvokeOnMainThreadAsync(() => IsWaitingLong = IsApplyingSettings);
        });
    }

    [RelayCommand]
    private void KeepWaiting()
    {
        if (!IsApplyingSettings || _applyWatchCts is null)
            return;
        StartWaitBackstop(_applyWatchCts.Token);
    }

    [RelayCommand]
    private void StopWaiting() => EndApplyReconnectWatch(succeeded: false);

    /// <summary>
    /// Dismisses the apply modal without a reconnect-failed alert. Used when a write
    /// fails before any reboot happened — the caller shows its own error dialog.
    /// Must be called on the main thread.
    /// </summary>
    private void AbortApplyReconnectWatch()
    {
        if (!IsApplyingSettings)
            return;

        IsApplyingSettings = false;
        _customToSend = null;
        _applyWatchCts?.Cancel();
        ProfileStatusMessage = null;
        MainThread.BeginInvokeOnMainThread(async () => await PopApplyModalAsync());
    }

    private static async Task PopApplyModalAsync()
    {
        try
        {
            if (Shell.Current.Navigation.ModalStack.LastOrDefault() is Views.ApplyingSettingsPage)
            {
                await Shell.Current.Navigation.PopModalAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DEVICESETTINGS] Apply modal pop failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Must be called on the main thread. <paramref name="confirm"/> false skips "Saved"
    /// when the gloves came back but the Custom settings didn't go.
    /// </summary>
    private void EndApplyReconnectWatch(bool succeeded, bool confirm = true)
    {
        if (!IsApplyingSettings)
            return;

        IsApplyingSettings = false;
        IsWaitingLong = false;
        _customToSend = null;
        _applyWatchCts?.Cancel();

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await PopApplyModalAsync();

            if (succeeded)
            {
                // The reboot is over — the "gloves are restarting" notice is done.
                if (ProfileStatusMessage == ProfileRebootWarning)
                {
                    ProfileStatusMessage = null;
                }
                if (confirm)
                    ConfirmSaved();
            }
            else
            {
                // A warning banner on the scan view, not a modal (Banner README).
                ProfileStatusMessage = null;
                ApplyReconnectFailed = true;
            }
        });
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
                // Therapy LED and battery arrive from the service's post-connect sync
                // (OnTherapyLedOffReceived / OnDeviceInfoReceived).
                ApplyReconnectFailed = false;
                // With Custom settings still to send, the INFO that follows ends the watch.
                if (_customToSend is null)
                    EndApplyReconnectWatch(succeeded: true);
            }
            else
            {
                TherapyLedLoaded = false;
                _resultBanner = null;
                FirmwareVersion = null;
                // Readings from the old link say nothing about the next one
                ApplyBatteryReading(null, null);
            }
            ResolveBanner();
            OnPropertyChanged(nameof(ShowDisconnectedView));
            // IsProfileDirty reads IsConnected, so pending-change state can shift
            // with the connection even though no setting changed.
            NotifyPendingChanged();
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
            _gloveControlService.DeviceInfoReceived -= OnDeviceInfoReceived;
            _gloveControlService.TherapyLedOffReceived -= OnTherapyLedOffReceived;
            _reconnectionService.ReconnectionStateChanged -= OnReconnectionStateChanged;
            WeakReferenceMessenger.Default.Unregister<CustomProfileSavedMessage>(this);
            _applyWatchCts?.Cancel();
            _applyWatchCts?.Dispose();
        }
        base.Dispose(disposing);
    }
}
