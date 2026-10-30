using System.Collections.ObjectModel;
using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.ConnectionStateManagement;
using BuzzahBuddy.Services.Glove;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BuzzahBuddy.Views.Controls;
using static BuzzahBuddy.Services.Glove.ErrorMessageHelper;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// One calibration finger button.
/// </summary>
public partial class FingerButtonItem : ObservableObject
{
    /// <summary>Protocol finger index sent to <see cref="IGloveControlService.BuzzFingerAsync"/>.</summary>
    public int Index { get; }

    /// <summary>Finger-only display label (e.g. "Index").</summary>
    public string Label { get; }

    /// <summary>AutomationId for UI testing (e.g. "PrimaryIndexButton").</summary>
    public string AutomationId { get; }

    /// <summary>"Primary Glove" or "Secondary Glove".</summary>
    public string Glove { get; }

    /// <summary>Tested fingers show a Lucide check on the button (bound in XAML).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleText))]
    private bool _isTested;

    /// <summary>The finger buzzing now shows Lucide's vibrate on its button until the result.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleText))]
    private bool _isBuzzing;

    /// <summary>"Primary glove index, tested" or "…, buzzing".</summary>
    public string AccessibleText =>
        $"{Glove} {Label.ToLowerInvariant()}{(IsBuzzing ? ", buzzing" : IsTested ? ", tested" : "")}";

    public FingerButtonItem(int index, string label, string automationId, string glove)
    {
        Index = index;
        Label = label;
        AutomationId = automationId;
        Glove = glove;
    }
}

/// <summary>
/// ViewModel for the calibration page.
/// Allows testing individual finger motors with adjustable intensity and duration. Test mode
/// starts when the page opens and ends when it closes (CalibrationPage), with no buttons.
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

    // Start on the controls' grids: strength steps of 20 (Stepper), lengths 100/500/1000/2000.
    [ObservableProperty]
    private int _intensity = 40;

    [ObservableProperty]
    private int _duration = 500;

    /// <summary>Buzz lengths for the SegmentedControl.</summary>
    public IList<SegmentOption> DurationOptions { get; } = new[]
    {
        new SegmentOption(100, "100ms", "100 milliseconds"), new SegmentOption(500, "500ms", "500 milliseconds"),
        new SegmentOption(1000, "1s", "1 second"), new SegmentOption(2000, "2s", "2 seconds"),
    };

    /// <summary>Set when a disconnect ended test mode; drives the warning banner.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNotConnected))]
    private bool _endedByDisconnect;

    public bool ShowNotConnected => !ConnectionInfo.IsConnected && !EndedByDisconnect;

    /// <summary>True while the page is on screen; test mode follows it.</summary>
    private bool _isOpen;

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
    private string _stepCaption = "Step 1 of 3";

    [ObservableProperty]
    private string _stepTitle = "Pick a Strength";

    [ObservableProperty]
    private string _stepDescription = "Start low. Go higher if you can't feel it.";

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

        // Pushed screens title the native bar beside Back (owner's choice over the DS headline).
        Title = "Test Motors";

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

            var item = new FingerButtonItem(i, shortLabel, fullLabel.Replace(" ", "") + "Button",
                i < count ? "Primary Glove" : "Secondary Glove");

            if (i < count)
                PrimaryFingers.Add(item);
            else
                SecondaryFingers.Add(item);
        }
    }

    /// <summary>Called from the page's OnAppearing: starts test mode.</summary>
    public Task OpenAsync()
    {
        _isOpen = true;
        return EnterCalibrationModeAsync();
    }

    /// <summary>Called from the page's OnDisappearing: ends test mode.</summary>
    public Task CloseAsync()
    {
        _isOpen = false;
        return ExitCalibrationModeAsync();
    }

    private async Task EnterCalibrationModeAsync()
    {
        if (!ConnectionInfo.IsConnected || IsInCalibrationMode || IsBusy)
            return; // the "Connect Your Primary Glove" banner says what to do

        IsBusy = true;

        try
        {
            await _gloveControlService.EnterCalibrationAsync();
            IsInCalibrationMode = true;

            // Left the page while starting: end it again.
            if (!_isOpen)
            {
                await ExitCalibrationModeAsync();
                return;
            }

            // Actuator count is populated on connect; rebuild in case it changed.
            RebuildFingerButtons();

            EndedByDisconnect = false;
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

    // The page is already closing, so a failure here has nowhere to show.
    private async Task ExitCalibrationModeAsync()
    {
        if (!IsInCalibrationMode)
            return;

        IsInCalibrationMode = false;
        try
        {
            await _gloveControlService.ExitCalibrationAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CALIBRATION] Exit failed: {ex.Message}");
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
    private async Task BuzzFingerAsync(int fingerIndex)
    {
        if (!IsInCalibrationMode)
            return; // test mode starts with the page; only a disconnect ends it, and its banner says so

        var finger = PrimaryFingers.Concat(SecondaryFingers).FirstOrDefault(f => f.Index == fingerIndex);
        if (finger is null)
        {
            await Shell.Current.DisplayAlert(
                "That Finger Isn't Available",
                "Pick one of the finger buttons on this screen.",
                "OK");
            return;
        }

        IsBusy = true;
        IsBuzzing = true;
        // The button shows this test's result only: vibrate now, then a check if it worked.
        finger.IsTested = false;
        finger.IsBuzzing = true;

        try
        {
            await _gloveControlService.BuzzFingerAsync(fingerIndex, Intensity, Duration);

            // Vibrate stays on the button for the buzz, then the check replaces it.
            await Task.Delay(Duration + 100);
            finger.IsBuzzing = false;
            finger.IsTested = true;
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
            finger.IsBuzzing = false;
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

            // A warning banner (not a modal) says test mode ended and how to get back.
            EndedByDisconnect = true;
        }
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateConnectionState();
            OnPropertyChanged(nameof(CanCalibrate));
            OnPropertyChanged(nameof(CanBuzzFingers));
            OnPropertyChanged(nameof(ShowNotConnected));

            // A different device (4- vs 5-motor) may have connected; actuator count
            // is refreshed by the post-connect INFO sync, and EnterCalibrationModeAsync
            // rebuilds again as a backstop before the buttons become usable.
            if (state == ConnectionState.Connected)
            {
                RebuildFingerButtons();
                // Back on this page after a disconnect: test mode picks up again by itself.
                if (_isOpen)
                    EnterCalibrationModeAsync().SafeFireAndForget("[CALIBRATION]");
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
                StepCaption = "Step 1 of 3";
                StepTitle = "Pick a Strength";
                StepDescription = "Start low. Go higher if you can't feel it.";
                break;
            case 2:
                StepCaption = "Step 2 of 3";
                StepTitle = "Pick a Buzz Length";
                StepDescription = "Longer buzzes are easier to notice.";
                break;
            case 3:
                StepCaption = "Step 3 of 3";
                StepTitle = "Test Each Finger";
                StepDescription = "Tap a finger to make it buzz. A check shows it's done.";
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
            foreach (var finger in PrimaryFingers.Concat(SecondaryFingers))
                finger.IsTested = false;
        }
        else
        {
            // Reset state when exiting calibration
            CurrentStep = 1;
            UpdateStepInfo();
            foreach (var finger in PrimaryFingers.Concat(SecondaryFingers))
                finger.IsTested = false;
        }
    }
}
