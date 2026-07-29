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
    private string _fingersText = string.Empty;

    [ObservableProperty]
    private bool _mirror;

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
    public string DerivedTimingText =>
        TryParseFields(out var fingers, out var onMs, out var offMs)
            ? TherapyTiming.OnOffRatioLabel(fingers, onMs, offMs)
            : "CR period unavailable";

    /// <summary>
    /// Note shown when the requested jitter exceeds what the current burst timing
    /// can actually accommodate — above the cap, raising jitter changes nothing.
    /// Null when there is nothing to warn about (the page binds visibility to it).
    /// </summary>
    public string? EffectiveJitterCapText
    {
        get
        {
            if (!TryParseFields(out _, out var onMs, out var offMs) ||
                !double.TryParse(JitterText, NumberStyles.Float, CultureInfo.InvariantCulture, out var jitter))
                return null;

            var cap = TherapyParameterBounds.EffectiveJitterCap(onMs, offMs);
            if (jitter <= cap)
                return null;

            return $"At this timing the gloves cap jitter near {cap.ToString("0.#", CultureInfo.InvariantCulture)}% " +
                   "so bursts don't run together.";
        }
    }

    /// <summary>Whether any field departs from the validated research configuration.</summary>
    public bool DeviatesFromResearchDefaults => DeviatingFields.Count > 0;

    private IReadOnlySet<string> DeviatingFields
    {
        get
        {
            try
            {
                return ResearchDefaults.DeviatingFields(BuildProfileFromFields(), MotorCount);
            }
            catch (FormatException)
            {
                // Mid-edit the fields may not parse; nothing meaningful to compare.
                return new HashSet<string>();
            }
            catch (OverflowException)
            {
                return new HashSet<string>();
            }
        }
    }

    partial void OnTimeOnMsTextChanged(string value) => RaiseDerived();
    partial void OnTimeOffMsTextChanged(string value) => RaiseDerived();
    partial void OnJitterTextChanged(string value) => RaiseDerived();
    partial void OnFingersTextChanged(string value) => RaiseDerived();
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
            PopulateFrom(profile);
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
    private void ResetToDefaults() => PopulateFrom(ResearchDefaults.For(MotorCount));

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

        TherapyProfile desired;
        try
        {
            desired = BuildProfileFromFields();
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
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

            applyStarted = true;
            await _gloveControlService.ApplyCustomProfileAsync(desired, baseline);

            // Re-read from the device so the form reflects what it accepted.
            var confirmed = await _gloveControlService.GetCurrentProfileAsync();
            PopulateFrom(confirmed);

            await Shell.Current.DisplayAlert(
                "Settings Saved",
                "The gloves are now using these settings, and will keep them after a restart.",
                "OK");
        }
        catch (ArgumentException ex)
        {
            // Validation happens before anything is sent, so no resync is needed.
            await Shell.Current.DisplayAlert("Invalid Value", ex.Message, "OK");
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
            PopulateFrom(current);
            return message + "\n\nSome changes may have been applied. The form now shows the gloves' current settings.";
        }
        catch
        {
            return message + "\n\nSome changes may have been applied. Reload from the gloves to see their current settings.";
        }
    }

    private void PopulateFrom(TherapyProfile profile)
    {
        var inv = CultureInfo.InvariantCulture;
        TimeOnMsText = (profile.TimeOn * 1000.0).ToString("0.#", inv);
        TimeOffMsText = (profile.TimeOff * 1000.0).ToString("0.#", inv);
        SessionMinutesText = profile.TimeSession.ToString(inv);
        AmplitudeMinText = profile.AmplitudeMin.ToString(inv);
        AmplitudeMaxText = profile.AmplitudeMax.ToString(inv);
        JitterText = profile.Jitter.ToString("0.#", inv);
        FingersText = profile.Fingers.ToString(inv);
        Mirror = profile.Mirror;
    }

    private TherapyProfile BuildProfileFromFields()
    {
        var inv = CultureInfo.InvariantCulture;
        return new TherapyProfile
        {
            ProfileId = TherapyProfile.CustomProfileId,
            TimeOn = double.Parse(TimeOnMsText, NumberStyles.Float, inv) / 1000.0,
            TimeOff = double.Parse(TimeOffMsText, NumberStyles.Float, inv) / 1000.0,
            TimeSession = int.Parse(SessionMinutesText, NumberStyles.Integer, inv),
            AmplitudeMin = int.Parse(AmplitudeMinText, NumberStyles.Integer, inv),
            AmplitudeMax = int.Parse(AmplitudeMaxText, NumberStyles.Integer, inv),
            Jitter = double.Parse(JitterText, NumberStyles.Float, inv),
            Fingers = int.Parse(FingersText, NumberStyles.Integer, inv),
            Mirror = Mirror,
        };
    }

    private bool TryParseFields(out int fingers, out double onMs, out double offMs)
    {
        var inv = CultureInfo.InvariantCulture;
        onMs = 0;
        offMs = 0;
        return int.TryParse(FingersText, NumberStyles.Integer, inv, out fingers)
            && double.TryParse(TimeOnMsText, NumberStyles.Float, inv, out onMs)
            && double.TryParse(TimeOffMsText, NumberStyles.Float, inv, out offMs);
    }
}
