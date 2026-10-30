using BuzzahBuddy.Messages;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Dialogs;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for editing the Custom therapy profile's parameters. The settings are
/// saved on the phone, with no glove needed; the Gloves page sends them
/// (PROFILE_CUSTOM) when the user applies the Custom profile.
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
    private readonly IDataStorageService _storageService;
    private readonly IConfirmationService _confirmation;

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

    /// <summary>True once the saved settings are in the form.</summary>
    [ObservableProperty]
    private bool _isLoaded;

    // Per-field errors, set when Save is tapped (not while typing; TextField README) and
    // cleared as soon as that field changes.
    [ObservableProperty] private string? _timeOnError;
    [ObservableProperty] private string? _timeOffError;
    [ObservableProperty] private string? _sessionError;
    [ObservableProperty] private string? _amplitudeMinError;
    [ObservableProperty] private string? _amplitudeMaxError;
    [ObservableProperty] private string? _jitterError;

    /// <summary>Shown when the settings differ from the study's.</summary>
    [ObservableProperty]
    private bool _deviatesBannerVisible;

    public ProfileSettingsViewModel(
        IGloveControlService gloveControlService,
        IDataStorageService storageService,
        IConfirmationService confirmation)
    {
        _gloveControlService = gloveControlService;
        _storageService = storageService;
        _confirmation = confirmation;

        // Pushed screens title the native bar beside Back (owner's choice over the DS headline).
        Title = "Custom Profile";
    }

    /// <summary>
    /// Motors per glove on the last connected device (4 on v2, 5 on v3; 4 until a glove
    /// reports). The derived timing and the study comparison use it; it is never sent.
    /// </summary>
    public int MotorCount => _gloveControlService.DeviceActuatorCount;

    // ========== Derived, read-only guidance ==========

    /// <summary>
    /// Cycle length implied by the current field values, e.g. "One full cycle takes
    /// 668ms, about 1.5 times a second." The studied protocol runs at 1.5 a second.
    /// </summary>
    public string DerivedTimingText => CurrentForm().DerivedTimingText;

    public string DerivedTimingSpoken => CurrentForm().DerivedTimingSpoken;

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
        Fingers = MotorCount,
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
        OnPropertyChanged(nameof(DerivedTimingText));
        OnPropertyChanged(nameof(DerivedTimingSpoken));
        OnPropertyChanged(nameof(EffectiveJitterCapText));
        OnPropertyChanged(nameof(DeviatesFromResearchDefaults));
        DeviatesBannerVisible = DeviatesFromResearchDefaults;
    }

    // ========== Commands ==========

    /// <summary>
    /// Fills the form with the saved settings, or the study's until the user saves any.
    /// Called once, from the page's OnAppearing.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoaded)
            return;

        PopulateFrom(await _storageService.GetCustomProfileAsync() ?? ResearchDefaults.For(MotorCount));
        IsLoaded = true;
    }

    /// <summary>
    /// Populates the form with the validated research configuration. Nothing is
    /// kept until Save.
    /// </summary>
    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        // Discards whatever the user has tuned. Destructive actions require a
        // confirmation (BlueBuzzah design system, accessibility.md) because an accidental tap is a real hazard
        // for tremor-prone users, and there is no undo once the fields are replaced.
        var confirmed = await _confirmation.ConfirmAsync(new ConfirmRequest(
            "Use the Study Settings?",
            "This fills the form with the settings from the published study. Nothing is kept until you tap Save.",
            "Use Study Settings"));

        if (confirmed)
            PopulateFrom(ResearchDefaults.For(MotorCount));
    }

    /// <summary>
    /// Validates the form, saves it on the phone and goes back. The Gloves page sends it
    /// when the user applies the Custom profile.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        // Each bad field gets its own message beside it; nothing is saved.
        var form = CurrentForm();
        var errors = form.Validate();
        TimeOnError = errors.GetValueOrDefault(CustomProfileField.TimeOn);
        TimeOffError = errors.GetValueOrDefault(CustomProfileField.TimeOff);
        SessionError = errors.GetValueOrDefault(CustomProfileField.Session);
        AmplitudeMinError = errors.GetValueOrDefault(CustomProfileField.AmplitudeMin);
        AmplitudeMaxError = errors.GetValueOrDefault(CustomProfileField.AmplitudeMax);
        JitterError = errors.GetValueOrDefault(CustomProfileField.Jitter);
        if (errors.Count > 0 || !form.TryBuildProfile(out var profile))
        {
            SemanticScreenReader.Announce(errors.Values.FirstOrDefault() ?? "Something Went Wrong");
            return;
        }

        profile.Name = "Custom";
        await _storageService.SaveCustomProfileAsync(profile);
        WeakReferenceMessenger.Default.Send(new CustomProfileSavedMessage());
        SemanticScreenReader.Announce("Custom settings saved");
        await Shell.Current.GoToAsync("..");
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
}
