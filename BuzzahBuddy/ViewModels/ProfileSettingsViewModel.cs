using System.Globalization;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Glove;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static BuzzahBuddy.Services.Glove.ErrorMessageHelper;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for editing the Custom therapy profile's parameters
/// (PROFILE_GET / PROFILE_CUSTOM).
/// <para>
/// Only the eight parameters the firmware persists for the Custom slot are
/// editable. Actuator type, drive frequency and pattern type are fixed: the
/// firmware rejects those keys on the Custom profile because it has nowhere to
/// store them.
/// </para>
/// </summary>
public partial class ProfileSettingsViewModel : BaseViewModel
{
    private readonly IGloveControlService _gloveControlService;

    /// <summary>
    /// Centralized connection state service exposed for XAML binding.
    /// </summary>
    public IConnectionStateService ConnectionInfo { get; }

    [ObservableProperty]
    private string _timeOnMsText = string.Empty;

    [ObservableProperty]
    private string _timeOffMsText = string.Empty;

    [ObservableProperty]
    private string _sessionMinutesText = string.Empty;

    [ObservableProperty]
    private string _amplitudeMinText = string.Empty;

    [ObservableProperty]
    private string _amplitudeMaxText = string.Empty;

    [ObservableProperty]
    private string _jitterText = string.Empty;

    [ObservableProperty]
    private bool _mirror;

    /// <summary>
    /// Active fingers as reported by the device. Not editable: every glove already
    /// drives all of its motors, and the count is a compile-time board constant
    /// (4 on BlueBuzzah, 5 on PentaBuzzer). It is carried here because the derived
    /// coordinated-reset timing depends on it, and round-tripped unchanged so the
    /// diff against the baseline never sends it.
    /// </summary>
    private int _deviceFingers = ResearchDefaults.Fingers;

    /// <summary>True once the current settings have been read from the device.</summary>
    [ObservableProperty]
    private bool _isLoaded;

    public ProfileSettingsViewModel(
        IGloveControlService gloveControlService,
        IConnectionStateService connectionStateService)
    {
        _gloveControlService = gloveControlService;
        ConnectionInfo = connectionStateService;

        Title = "Custom Profile";
    }

    /// <summary>Motors per glove on the connected device (4 on v2, 5 on v3).</summary>
    public int MotorCount => _gloveControlService.DeviceActuatorCount;

    // ========== Derived, read-only guidance ==========

    /// <summary>
    /// Coordinated-reset period and frequency implied by the current field values,
    /// e.g. "CR period 668 ms · 1.50 Hz". The studied protocol runs at 1.50 Hz.
    /// </summary>
    public string DerivedTimingText => CurrentForm().DerivedTimingText;

    /// <summary>
    /// Note shown when the requested jitter exceeds what the current burst timing
    /// can actually accommodate — above the cap, raising jitter changes nothing.
    /// Null when there is nothing to warn about (the page binds visibility to it).
    /// </summary>
    public string? EffectiveJitterCapText => CurrentForm().EffectiveJitterCapText;

    /// <summary>Whether any field departs from the validated research configuration.</summary>
    public bool DeviatesFromResearchDefaults => CurrentForm().DeviatingFields(MotorCount).Count > 0;

    /// <summary>
    /// Snapshots the entries into the pure form model that owns the parsing,
    /// unit conversion, and deviation logic.
    /// </summary>
    private CustomProfileForm CurrentForm() => new()
    {
        TimeOnMsText = TimeOnMsText,
        TimeOffMsText = TimeOffMsText,
        SessionMinutesText = SessionMinutesText,
        AmplitudeMinText = AmplitudeMinText,
        AmplitudeMaxText = AmplitudeMaxText,
        JitterText = JitterText,
        Mirror = Mirror,
        Fingers = _deviceFingers,
    };

    partial void OnTimeOnMsTextChanged(string value) => RaiseDerived();
    partial void OnTimeOffMsTextChanged(string value) => RaiseDerived();
    partial void OnJitterTextChanged(string value) => RaiseDerived();
    partial void OnSessionMinutesTextChanged(string value) => RaiseDerived();
    partial void OnAmplitudeMinTextChanged(string value) => RaiseDerived();
    partial void OnAmplitudeMaxTextChanged(string value) => RaiseDerived();
    partial void OnMirrorChanged(bool value) => RaiseDerived();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(DerivedTimingText));
        OnPropertyChanged(nameof(EffectiveJitterCapText));
        OnPropertyChanged(nameof(DeviatesFromResearchDefaults));
    }

    // ========== Commands ==========

    /// <summary>
    /// Reads the current profile parameters from the device and populates the form.
    /// Called from the page's OnAppearing.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
            return;

        if (!ConnectionInfo.IsConnected)
        {
            IsLoaded = false;
            return;
        }

        IsBusy = true;
        try
        {
            var profile = await _gloveControlService.GetCurrentProfileAsync();
            PopulateFromDevice(profile);
            IsLoaded = true;
            OnPropertyChanged(nameof(MotorCount));
        }
        catch (BlueBuzzahCommandException ex)
        {
            var (title, message) = GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert(GetErrorTitle(ex), GetErrorMessage(ex), "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Populates the form with the validated research configuration. Local only —
    /// nothing reaches the gloves until Apply.
    /// </summary>
    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        // Discards whatever the user has tuned. CLAUDE.md requires a confirmation
        // for destructive actions because an accidental tap is a real hazard for
        // tremor-prone users, and there is no undo once the fields are replaced.
        var confirmed = await Shell.Current.DisplayAlert(
            "Use Research Defaults?",
            "This replaces the values in the form with the settings used in the published "
            + "vibrotactile therapy study. Nothing is sent to the gloves until you save.",
            "Use Defaults",
            "Cancel");

        if (confirmed)
            PopulateFrom(ResearchDefaults.For(MotorCount));
    }

    /// <summary>
    /// Validates the form and sends the changed parameters to the device.
    /// </summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (IsBusy)
            return;

        if (!IsLoaded || !ConnectionInfo.IsConnected)
        {
            await Shell.Current.DisplayAlert(
                "Not Connected",
                "Connect to a BlueBuzzah glove and load the current settings first.",
                "OK");
            return;
        }

        if (!CurrentForm().TryBuildProfile(out var desired))
        {
            await Shell.Current.DisplayAlert(
                "Invalid Value",
                "One or more fields contain an invalid number. Please check the values and try again.",
                "OK");
            return;
        }

        IsBusy = true;
        var applyStarted = false;
        try
        {
            // Read the baseline fresh so the diff isn't computed against stale
            // values (another profile may have been loaded, or the device
            // rebooted, while this page was open).
            var baseline = await _gloveControlService.GetCurrentProfileAsync();

            // Re-sync the finger count from the device we are about to write to.
            // Without this, a silent reconnect to different hardware while this page
            // stayed on screen would send the previous glove's count — on a 5-motor
            // device that silently drops it to 4 active fingers.
            _deviceFingers = baseline.Fingers;

            // Range-check before flagging the send. This is pure string building
            // that touches no hardware, so an out-of-range value throws here with
            // applyStarted still false — which is what lets the catch blocks tell
            // "nothing was written" apart from "a batch was interrupted".
            GloveControlService.BuildCustomProfileParameters(
                desired, baseline, _gloveControlService.DeviceActuatorCount);

            applyStarted = true;
            await _gloveControlService.ApplyCustomProfileAsync(desired, baseline);

            // Re-read from the device so the form reflects what it accepted.
            var confirmed = await _gloveControlService.GetCurrentProfileAsync();
            PopulateFromDevice(confirmed);

            // Only promise persistence when the firmware actually provides it.
            await Shell.Current.DisplayAlert(
                "Settings Saved",
                _gloveControlService.PersistsCustomProfile
                    ? "The gloves are now using these settings, and will keep them after a restart."
                    : "The gloves are now using these settings. This firmware does not store them, "
                      + "so they will return to the previous values when the gloves restart.",
                "OK");
        }
        catch (ArgumentException ex)
        {
            // Parameter validation runs before the first send, so in the reachable
            // case nothing has been written and the form is still accurate. Resync
            // anyway when a send had already begun: the command-length guard also
            // throws ArgumentException, and it fires mid-batch. That guard is
            // unreachable at present (worst-case command is ~98 of 255 chars), so
            // this costs nothing today and stops a future ninth parameter from
            // turning a partial write into a silently stale form.
            await Shell.Current.DisplayAlert(
                "Invalid Value", await AppendResyncNoteAsync(ex.Message, applyStarted), "OK");
        }
        catch (BlueBuzzahCommandException ex)
        {
            var (title, message) = GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(title, await AppendResyncNoteAsync(message, applyStarted), "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert(
                GetErrorTitle(ex), await AppendResyncNoteAsync(GetErrorMessage(ex), applyStarted), "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// After a failed apply, some parameters may already be on the device (chunked
    /// sends aren't atomic). Best-effort re-read so the form shows the gloves'
    /// actual state, and extend the error message accordingly.
    /// </summary>
    private async Task<string> AppendResyncNoteAsync(string message, bool applyStarted)
    {
        if (!applyStarted)
            return message;

        try
        {
            var current = await _gloveControlService.GetCurrentProfileAsync();
            PopulateFromDevice(current);
            return message + "\n\nSome changes may have been applied. The form now shows the gloves' current settings.";
        }
        catch
        {
            return message + "\n\nSome changes may have been applied. Reload from the gloves to see their current settings.";
        }
    }

    private void PopulateFrom(TherapyProfile profile)
    {
        var form = new CustomProfileForm();
        form.PopulateFrom(profile);

        TimeOnMsText = form.TimeOnMsText;
        TimeOffMsText = form.TimeOffMsText;
        SessionMinutesText = form.SessionMinutesText;
        AmplitudeMinText = form.AmplitudeMinText;
        AmplitudeMaxText = form.AmplitudeMaxText;
        JitterText = form.JitterText;
        Mirror = form.Mirror;
    }

    /// <summary>
    /// Populates the form from a profile read off the device, including the
    /// non-editable finger count. Only device reads may change that count —
    /// "Use Research Defaults" must not silently alter it, since the form shows
    /// no field the user could see it change in.
    /// </summary>
    private void PopulateFromDevice(TherapyProfile profile)
    {
        _deviceFingers = profile.Fingers;
        PopulateFrom(profile);

        // The derived timing depends on _deviceFingers, which is not observable.
        // PopulateFrom's property setters usually raise it as a side effect, but
        // only when a string value actually changes — a re-read that returns
        // identical text with a different finger count would otherwise leave the
        // displayed CR period stale.
        RaiseDerived();
    }

}
