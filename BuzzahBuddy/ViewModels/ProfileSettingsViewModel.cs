using System.ComponentModel;
using System.Globalization;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Dialogs;
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
    private readonly IConfirmationService _confirmation;
    private (StatusTone Tone, string Title, string Message, string? Detail)? _saveResult;

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

    partial void OnIsLoadedChanged(bool value) => ResolveBanner();

    // Per-field errors, set when Save is tapped (not while typing; TextField README) and
    // cleared as soon as that field changes.
    [ObservableProperty] private string? _timeOnError;
    [ObservableProperty] private string? _timeOffError;
    [ObservableProperty] private string? _sessionError;
    [ObservableProperty] private string? _amplitudeMinError;
    [ObservableProperty] private string? _amplitudeMaxError;
    [ObservableProperty] private string? _jitterError;

    [ObservableProperty]
    private bool _savedBadgeVisible;

    // One banner: not connected > save failed/not stored > differs from the study.
    [ObservableProperty] private bool _bannerVisible;
    [ObservableProperty] private StatusTone _bannerTone = StatusTone.Info;
    [ObservableProperty] private string? _bannerTitle;
    [ObservableProperty] private string? _bannerMessage;
    [ObservableProperty] private string? _bannerDetail;

    public ProfileSettingsViewModel(
        IGloveControlService gloveControlService,
        IConnectionStateService connectionStateService,
        IConfirmationService confirmation)
    {
        _gloveControlService = gloveControlService;
        _confirmation = confirmation;
        ConnectionInfo = connectionStateService;

        // The page's own headline is the title; the native bar only carries Back.
        Title = string.Empty;
        ConnectionInfo.PropertyChanged += OnConnectionInfoChanged;
        ResolveBanner();
    }

    // A disconnect while this page is open swaps in the "Connect Your Primary Glove" banner.
    private void OnConnectionInfoChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IConnectionStateService.IsConnected))
            MainThread.BeginInvokeOnMainThread(ResolveBanner);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ConnectionInfo.PropertyChanged -= OnConnectionInfoChanged;
        base.Dispose(disposing);
    }

    /// <summary>Motors per glove on the connected device (4 on v2, 5 on v3).</summary>
    public int MotorCount => _gloveControlService.DeviceActuatorCount;

    // ========== Derived, read-only guidance ==========

    /// <summary>
    /// Cycle length implied by the current field values, e.g. "One full cycle takes
    /// 668 ms, about 1.5 times a second." The studied protocol runs at 1.5 a second.
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

    partial void OnTimeOnMsTextChanged(string value) { TimeOnError = null; RaiseDerived(); }
    partial void OnTimeOffMsTextChanged(string value) { TimeOffError = null; RaiseDerived(); }
    partial void OnJitterTextChanged(string value) { JitterError = null; RaiseDerived(); }
    partial void OnSessionMinutesTextChanged(string value) { SessionError = null; RaiseDerived(); }
    partial void OnAmplitudeMinTextChanged(string value) { AmplitudeMinError = null; AmplitudeMaxError = null; RaiseDerived(); }
    partial void OnAmplitudeMaxTextChanged(string value) { AmplitudeMaxError = null; RaiseDerived(); }
    partial void OnMirrorChanged(bool value) => RaiseDerived();

    private void RaiseDerived()
    {
        SavedBadgeVisible = false;
        OnPropertyChanged(nameof(DerivedTimingText));
        OnPropertyChanged(nameof(EffectiveJitterCapText));
        OnPropertyChanged(nameof(DeviatesFromResearchDefaults));
        ResolveBanner();
    }

    private void ResolveBanner()
    {
        (StatusTone Tone, string Title, string Message, string? Detail)? banner =
            !IsLoaded || !ConnectionInfo.IsConnected
                ? (StatusTone.Info, "Connect Your Primary Glove", "Connect on the Gloves tab to see and change these settings.", null)
            : _saveResult is { } result ? result
            : DeviatesFromResearchDefaults
                ? (StatusTone.Warning, "These Settings Differ from the Study",
                    "They're within what the gloves accept, but no study has tested them.", null)
            : null;
        BannerVisible = banner is not null;
        if (banner is { } b)
            (BannerTone, BannerTitle, BannerMessage, BannerDetail) = b;
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
            var error = GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
        }
        catch (Exception ex)
        {
            var error = FromException(ex);
            await Shell.Current.DisplayAlert(error.Title, error.ForAlert, "OK");
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
        // Discards whatever the user has tuned. Destructive actions require a
        // confirmation (BlueBuzzah design system, accessibility.md) because an accidental tap is a real hazard
        // for tremor-prone users, and there is no undo once the fields are replaced.
        var confirmed = await _confirmation.ConfirmAsync(new ConfirmRequest(
            "Use the Study Settings?",
            "This fills the form with the settings from the published study. Nothing changes on your gloves until you tap Save to Gloves.",
            "Use Study Settings"));

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
            ResolveBanner(); // "Connect Your Primary Glove"
            return;
        }

        // Each bad field gets its own message beside it; nothing is sent.
        var errors = CurrentForm().Validate();
        TimeOnError = errors.GetValueOrDefault(CustomProfileField.TimeOn);
        TimeOffError = errors.GetValueOrDefault(CustomProfileField.TimeOff);
        SessionError = errors.GetValueOrDefault(CustomProfileField.Session);
        AmplitudeMinError = errors.GetValueOrDefault(CustomProfileField.AmplitudeMin);
        AmplitudeMaxError = errors.GetValueOrDefault(CustomProfileField.AmplitudeMax);
        JitterError = errors.GetValueOrDefault(CustomProfileField.Jitter);
        if (errors.Count > 0 || !CurrentForm().TryBuildProfile(out var desired))
        {
            SemanticScreenReader.Announce(errors.Values.FirstOrDefault() ?? "Something Went Wrong");
            return;
        }

        _saveResult = null;

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
            if (_gloveControlService.PersistsCustomProfile)
            {
                SavedBadgeVisible = true;
                SemanticScreenReader.Announce("Settings saved to both gloves");
            }
            else
            {
                _saveResult = (StatusTone.Info, "Saved until the Gloves Restart",
                    "This firmware doesn't store Custom settings. Update it with the BlueBuzzah Updater to keep them.", null);
            }
        }
        catch (Exception ex)
        {
            // Covers BlueBuzzahCommandException (firmware text), timeouts and the
            // client-side parameter guards (ArgumentException: the command-length guard
            // fires mid-batch, so resync when a send had begun). Raw text goes in Detail.
            var error = FromException(ex);
            _saveResult = (StatusTone.Danger, "Couldn't save your settings",
                await AppendResyncNoteAsync(error.Message, applyStarted), error.Detail);
        }
        finally
        {
            IsBusy = false;
            ResolveBanner();
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
            return message + " Some changes may have been saved. The form shows what your gloves have now.";
        }
        catch
        {
            return message + " Some changes may have been saved. Tap Reload from gloves to see what your gloves have now.";
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
