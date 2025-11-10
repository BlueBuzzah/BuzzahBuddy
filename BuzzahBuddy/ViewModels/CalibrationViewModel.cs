using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using static BuzzahBuddy.Services.Glove.ErrorMessageHelper;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// ViewModel for the calibration page.
/// Allows testing individual finger motors with adjustable intensity and duration.
/// </summary>
public partial class CalibrationViewModel : BaseViewModel
{
    private readonly IGloveControlService _gloveControlService;
    private readonly IBluetoothService _bluetoothService;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
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
    private bool _isBuzzing;

    // Wizard step tracking (1 = Intensity, 2 = Duration, 3 = Test Fingers)
    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private string _stepTitle = "Step 1 of 3: Select Intensity";

    [ObservableProperty]
    private string _stepDescription = "Choose the vibration intensity level for testing.";

    // Track which fingers have been tested
    private readonly HashSet<int> _testedFingers = new();

    public bool IsFingerTested(int fingerIndex) => _testedFingers.Contains(fingerIndex);

    public CalibrationViewModel(
        IGloveControlService gloveControlService,
        IBluetoothService bluetoothService)
    {
        _gloveControlService = gloveControlService;
        _bluetoothService = bluetoothService;

        Title = "Calibration";

        // Subscribe to connection events
        _bluetoothService.ConnectionStateChanged += OnConnectionStateChanged;

        // Initialize connection state
        UpdateConnectionState();
    }

    [RelayCommand]
    private async Task EnterCalibrationModeAsync()
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
            await _gloveControlService.EnterCalibrationAsync();
            IsInCalibrationMode = true;

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

        if (fingerIndex < 0 || fingerIndex > 7)
        {
            await Shell.Current.DisplayAlert(
                "Invalid Finger",
                "Finger index must be 0-7.",
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
        IsConnected = _bluetoothService.CurrentConnectionState == ConnectionState.Connected;

        // Exit calibration mode if disconnected
        if (!IsConnected && IsInCalibrationMode)
        {
            IsInCalibrationMode = false;
            SelectedFingerIndex = -1;
            SelectedFingerName = "None";
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateConnectionState();
        });
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

    private static string GetFingerName(int fingerIndex)
    {
        return fingerIndex switch
        {
            0 => "Left Thumb",
            1 => "Left Index",
            2 => "Left Middle",
            3 => "Left Ring",
            4 => "Right Thumb",
            5 => "Right Index",
            6 => "Right Middle",
            7 => "Right Ring",
            _ => "Unknown"
        };
    }
}
