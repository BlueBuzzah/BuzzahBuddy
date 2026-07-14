using System.Globalization;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Glove;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static BuzzahBuddy.Services.Glove.ErrorMessageHelper;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for viewing and editing the currently loaded therapy profile's
/// parameters on the device (PROFILE_GET / PROFILE_CUSTOM).
/// Edits are applied to the running firmware but are not persisted — they last
/// until the gloves restart or another profile is loaded.
/// </summary>
public partial class ProfileSettingsViewModel : BaseViewModel
{
    private readonly IGloveControlService _gloveControlService;

    /// <summary>
    /// Centralized connection state service exposed for XAML binding.
    /// </summary>
    public IConnectionStateService ConnectionInfo { get; }

    public IReadOnlyList<string> ActuatorTypeOptions { get; } = new[] { "LRA", "ERM" };
    public IReadOnlyList<string> PatternOptions { get; } = new[] { "Random", "Sequential", "Mirrored" };

    [ObservableProperty]
    private string _actuatorType = "LRA";

    [ObservableProperty]
    private string _pattern = "Random";

    [ObservableProperty]
    private string _frequencyText = string.Empty;

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

    /// <summary>True once the current settings have been read from the device.</summary>
    [ObservableProperty]
    private bool _isLoaded;

    public ProfileSettingsViewModel(
        IGloveControlService gloveControlService,
        IConnectionStateService connectionStateService)
    {
        _gloveControlService = gloveControlService;
        ConnectionInfo = connectionStateService;

        Title = "Profile Settings";
    }

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
            desired = BuildDesiredProfile();
        }
        catch (FormatException)
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
                "Settings Applied",
                "The gloves are now using the updated settings.\n\n" +
                "These changes last until the gloves restart or another profile is loaded.",
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
        ActuatorType = profile.ActuatorType.Equals("ERM", StringComparison.OrdinalIgnoreCase) ? "ERM" : "LRA";
        Pattern = profile.PatternType.ToUpperInvariant() switch
        {
            "SEQ" or "SEQUENTIAL" => "Sequential",
            "MIRRORED" => "Mirrored",
            _ => "Random",
        };
        FrequencyText = profile.ActuatorFrequency.ToString(inv);
        TimeOnMsText = (profile.TimeOn * 1000.0).ToString("0.#", inv);
        TimeOffMsText = (profile.TimeOff * 1000.0).ToString("0.#", inv);
        SessionMinutesText = profile.TimeSession.ToString(inv);
        AmplitudeMinText = profile.AmplitudeMin.ToString(inv);
        AmplitudeMaxText = profile.AmplitudeMax.ToString(inv);
        JitterText = profile.Jitter.ToString("0.#", inv);
        Mirror = profile.Mirror;
    }

    private TherapyProfile BuildDesiredProfile()
    {
        var inv = CultureInfo.InvariantCulture;
        return new TherapyProfile
        {
            ActuatorType = ActuatorType,
            ActuatorFrequency = int.Parse(FrequencyText, NumberStyles.Integer, inv),
            TimeOn = double.Parse(TimeOnMsText, NumberStyles.Float, inv) / 1000.0,
            TimeOff = double.Parse(TimeOffMsText, NumberStyles.Float, inv) / 1000.0,
            TimeSession = int.Parse(SessionMinutesText, NumberStyles.Integer, inv),
            AmplitudeMin = int.Parse(AmplitudeMinText, NumberStyles.Integer, inv),
            AmplitudeMax = int.Parse(AmplitudeMaxText, NumberStyles.Integer, inv),
            Jitter = double.Parse(JitterText, NumberStyles.Float, inv),
            Mirror = Mirror,
            PatternType = Pattern switch
            {
                "Sequential" => "SEQUENTIAL",
                "Mirrored" => "MIRRORED",
                _ => "RNDP",
            },
        };
    }
}
