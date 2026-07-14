using System.Collections.ObjectModel;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Glove;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static BuzzahBuddy.Services.Glove.ErrorMessageHelper;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// One calibration finger button.
/// </summary>
/// <param name="Index">Protocol finger index sent to <see cref="IGloveControlService.BuzzFingerAsync"/>.</param>
/// <param name="Label">Finger-only display label used as the button's visible text (e.g. "Index").</param>
/// <param name="AutomationId">AutomationId for UI testing (e.g. "PrimaryIndexButton").</param>
public record FingerButtonItem(int Index, string Label, string AutomationId);

/// <summary>
/// ViewModel for the calibration page.
/// Allows testing individual finger motors with adjustable intensity and duration.
/// </summary>
public partial class CalibrationViewModel : BaseViewModel
{
    private readonly IGloveControlService _gloveControlService;
    private readonly IBluetoothService _bluetoothService;

    /// <summary>
    /// Centralized connection state service exposed for XAML binding.
    /// </summary>
    public IConnectionStateService ConnectionInfo { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCalibrate))]
    [NotifyPropertyChangedFor(nameof(CanBuzzFingers))]
    private bool _isInCalibrationMode;

    [ObservableProperty]
    private int _intensity = 50;

    [ObservableProperty]
    private int _duration = 200;

    [ObservableProperty]
    private int _selectedFingerIndex = -1;

    [ObservableProperty]
    private string _selectedFingerName = "None";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuzzFingers))]
    private bool _isBuzzing;

    // Wizard step tracking (1 = Intensity, 2 = Duration, 3 = Test Fingers)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _currentStep = 1;

    [ObservableProperty]
    private string _stepTitle = "Step 1 of 3: Select Intensity";

    [ObservableProperty]
    private string _stepDescription = "Choose the vibration intensity level for testing.";

    // Track which fingers have been tested
    private readonly HashSet<int> _testedFingers = new();

    public bool IsFingerTested(int fingerIndex) => _testedFingers.Contains(fingerIndex);

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool CanGoPrevious => CurrentStep > 1;
    public bool CanGoNext => CurrentStep < 3;

    /// <summary>Connected and in calibration mode — gates intensity/duration buttons.</summary>
    public bool CanCalibrate => ConnectionInfo.IsConnected && IsInCalibrationMode;

    /// <summary>Like CanCalibrate, but also blocks taps while a buzz command is in flight.</summary>
    public bool CanBuzzFingers => CanCalibrate && !IsBuzzing;

    /// <summary>
    /// Finger-test buttons for the primary glove, sized to the connected device's actuator count.
    /// </summary>
    public ObservableCollection<FingerButtonItem> PrimaryFingers { get; } = new();

    /// <summary>
    /// Finger-test buttons for the secondary glove, sized to the connected device's actuator count.
    /// </summary>
    public ObservableCollection<FingerButtonItem> SecondaryFingers { get; } = new();

    public CalibrationViewModel(
        IGloveControlService gloveControlService,
        IBluetoothService bluetoothService,
        IConnectionStateService connectionStateService)
    {
        _gloveControlService = gloveControlService;
        _bluetoothService = bluetoothService;
        ConnectionInfo = connectionStateService;

        Title = "Calibration";

        // Subscribe to connection events
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        // Build the finger-test button collections from the current actuator count
        RebuildFingerButtons();

        // Initialize connection state
        UpdateConnectionState();
    }

    /// <summary>
    /// (Re)builds the <see cref="PrimaryFingers"/> and <see cref="SecondaryFingers"/> collections
    /// from <see cref="IGloveControlService.DeviceActuatorCount"/> and <see cref="CalibrationSettings.GetFingerLabel"/>.
    /// </summary>
    private void RebuildFingerButtons()
    {
        var count = _gloveControlService.DeviceActuatorCount;
        PrimaryFingers.Clear();
        SecondaryFingers.Clear();

        for (int i = 0; i < 2 * count; i++)
        {
            // Full label is "{Glove} {Finger}" (e.g. "Primary Index"); the button shows just the finger part.
            // Guard against GetFingerLabel's out-of-range fallback ("Finger N"), which has no space to split on
            // beyond index 0 — Split(' ')[1] would throw in that case.
            var fullLabel = CalibrationSettings.GetFingerLabel(i, count);
            var spaceIndex = fullLabel.IndexOf(' ');
            var shortLabel = spaceIndex >= 0 && spaceIndex < fullLabel.Length - 1
                ? fullLabel[(spaceIndex + 1)..]
                : fullLabel;

            var item = new FingerButtonItem(i, shortLabel, fullLabel.Replace(" ", "") + "Button");

            if (i < count)
                PrimaryFingers.Add(item);
            else
                SecondaryFingers.Add(item);
        }
    }

    [RelayCommand]
    private async Task EnterCalibrationModeAsync()
    {
        if (!ConnectionInfo.IsConnected)
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
            await _gloveControlService.EnterCalibrationAsync();
            IsInCalibrationMode = true;

            // Actuator count is populated on connect; rebuild in case it changed.
            RebuildFingerButtons();

            await Shell.Current.DisplayAlert(
                "Calibration Mode Active",
                "You can now test individual finger motors.\n\n" +
                "Select a finger, adjust intensity and duration, then tap the finger button to test.",
                "OK");
        }
        catch (BlueBuzzahCommandException ex)
        {
            var (title, message) = GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        catch (Exception ex)
        {
            var title = GetErrorTitle(ex);
            var message = GetErrorMessage(ex);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExitCalibrationModeAsync()
    {
        if (!IsInCalibrationMode)
            return;

        IsBusy = true;

        try
        {
            await _gloveControlService.ExitCalibrationAsync();
            IsInCalibrationMode = false;
            SelectedFingerIndex = -1;
            SelectedFingerName = "None";

            await Shell.Current.DisplayAlert(
                "Calibration Complete",
                "Exited calibration mode. You can now return to normal therapy sessions.",
                "OK");
        }
        catch (BlueBuzzahCommandException ex)
        {
            var (title, message) = GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        catch (Exception ex)
        {
            var title = GetErrorTitle(ex);
            var message = GetErrorMessage(ex);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SetIntensity(int value)
    {
        Intensity = value;
    }

    [RelayCommand]
    private void SetDuration(int value)
    {
        Duration = value;
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep < 3)
        {
            CurrentStep++;
            UpdateStepInfo();
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            UpdateStepInfo();
        }
    }

    [RelayCommand]
    private async Task SkipCalibrationAsync()
    {
        // Use safe default values
        Intensity = 50;
        Duration = 200;

        var result = await Shell.Current.DisplayAlert(
            "Skip Calibration?",
            "This will use safe default settings (50% intensity, 200ms duration) without testing individual motors.\n\nYou can always return to calibration later if needed.",
            "Use Defaults",
            "Cancel");

        if (result)
        {
            await ExitCalibrationModeAsync();
        }
    }

    [RelayCommand]
    private async Task BuzzFingerAsync(int fingerIndex)
    {
        if (!IsInCalibrationMode)
        {
            await Shell.Current.DisplayAlert(
                "Not in Calibration Mode",
                "Please enter calibration mode first.",
                "OK");
            return;
        }

        var maxFingerIndex = (_gloveControlService.DeviceActuatorCount * 2) - 1;
        if (fingerIndex < 0 || fingerIndex > maxFingerIndex)
        {
            await Shell.Current.DisplayAlert(
                "Invalid Finger",
                $"Finger index must be 0-{maxFingerIndex}.",
                "OK");
            return;
        }

        // Update selected finger
        SelectedFingerIndex = fingerIndex;
        SelectedFingerName = GetFingerName(fingerIndex);

        IsBusy = true;
        IsBuzzing = true;

        try
        {
            await _gloveControlService.BuzzFingerAsync(fingerIndex, Intensity, Duration);

            // Mark finger as tested
            _testedFingers.Add(fingerIndex);

            // Brief delay to show visual feedback
            await Task.Delay(Duration + 100);
        }
        catch (BlueBuzzahCommandException ex)
        {
            var (title, message) = GetFriendlyError(ex.Message);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        catch (Exception ex)
        {
            var title = GetErrorTitle(ex);
            var message = GetErrorMessage(ex);
            await Shell.Current.DisplayAlert(title, message, "OK");
        }
        finally
        {
            IsBusy = false;
            IsBuzzing = false;
        }
    }

    partial void OnIntensityChanged(int value)
    {
        // Clamp to valid range
        if (value < 0)
            Intensity = 0;
        else if (value > 100)
            Intensity = 100;
    }

    partial void OnDurationChanged(int value)
    {
        // Clamp to valid range (50-2000ms)
        if (value < 50)
            Duration = 50;
        else if (value > 2000)
            Duration = 2000;
    }

    private void UpdateConnectionState()
    {
        if (!ConnectionInfo.IsConnected && IsInCalibrationMode)
        {
            IsInCalibrationMode = false;
            SelectedFingerIndex = -1;
            SelectedFingerName = "None";

            // Notify user that calibration was interrupted
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Shell.Current.DisplayAlert(
                    "Device Disconnected",
                    "Calibration mode has been exited because the device was disconnected.",
                    "OK");
            });
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateConnectionState();
            OnPropertyChanged(nameof(CanCalibrate));
            OnPropertyChanged(nameof(CanBuzzFingers));

            // A different device (4- vs 5-motor) may have connected; actuator count
            // is refreshed by the post-connect INFO sync, and EnterCalibrationModeAsync
            // rebuilds again as a backstop before the buttons become usable.
            if (state == ConnectionState.Connected)
            {
                RebuildFingerButtons();
            }
        });
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bluetoothService.ConnectionStateChanged -= OnConnectionStateChanged;
        }
        base.Dispose(disposing);
    }

    private void UpdateStepInfo()
    {
        switch (CurrentStep)
        {
            case 1:
                StepTitle = "Step 1 of 3: Select Intensity";
                StepDescription = "Choose the vibration intensity level for testing.";
                break;
            case 2:
                StepTitle = "Step 2 of 3: Select Duration";
                StepDescription = "Choose how long each test pulse will last.";
                break;
            case 3:
                StepTitle = "Step 3 of 3: Test Fingers";
                StepDescription = "Tap each finger button to test the motor. Tested fingers will show a checkmark.";
                break;
        }
    }

    partial void OnIsInCalibrationModeChanged(bool value)
    {
        if (value)
        {
            // Reset wizard to step 1
            CurrentStep = 1;
            UpdateStepInfo();
            _testedFingers.Clear();
        }
        else
        {
            // Reset state when exiting calibration
            CurrentStep = 1;
            UpdateStepInfo();
            _testedFingers.Clear();
        }
    }

    private string GetFingerName(int fingerIndex)
    {
        return CalibrationSettings.GetFingerLabel(fingerIndex, _gloveControlService.DeviceActuatorCount);
    }
}
