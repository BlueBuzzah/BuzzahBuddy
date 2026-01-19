using BuzzahBuddy.Models;

namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// A reusable banner control that displays Bluetooth connection status
/// with appropriate icons, colors, and optional action links.
/// </summary>
public partial class ConnectionStatusBanner : ContentView
{
    #region Bindable Properties

    public static readonly BindableProperty ConnectionStateProperty = BindableProperty.Create(
        nameof(ConnectionState),
        typeof(ConnectionState),
        typeof(ConnectionStatusBanner),
        ConnectionState.Disconnected,
        propertyChanged: OnConnectionStateChanged);

    public static readonly BindableProperty DeviceNameProperty = BindableProperty.Create(
        nameof(DeviceName),
        typeof(string),
        typeof(ConnectionStatusBanner),
        string.Empty,
        propertyChanged: OnDisplayPropertyChanged);

    public static readonly BindableProperty BatteryLevelProperty = BindableProperty.Create(
        nameof(BatteryLevel),
        typeof(int?),
        typeof(ConnectionStatusBanner),
        null,
        propertyChanged: OnDisplayPropertyChanged);

    public static readonly BindableProperty IsBluetoothEnabledProperty = BindableProperty.Create(
        nameof(IsBluetoothEnabled),
        typeof(bool),
        typeof(ConnectionStatusBanner),
        true,
        propertyChanged: OnDisplayPropertyChanged);

    #endregion

    #region Public Properties

    public ConnectionState ConnectionState
    {
        get => (ConnectionState)GetValue(ConnectionStateProperty);
        set => SetValue(ConnectionStateProperty, value);
    }

    public string DeviceName
    {
        get => (string)GetValue(DeviceNameProperty);
        set => SetValue(DeviceNameProperty, value);
    }

    public int? BatteryLevel
    {
        get => (int?)GetValue(BatteryLevelProperty);
        set => SetValue(BatteryLevelProperty, value);
    }

    public bool IsBluetoothEnabled
    {
        get => (bool)GetValue(IsBluetoothEnabledProperty);
        set => SetValue(IsBluetoothEnabledProperty, value);
    }

    #endregion

    #region Computed Display Properties

    public Color BannerBackgroundColor => GetBannerBackgroundColor();
    public bool IsConnecting => ConnectionState == ConnectionState.Connecting;
    public string StatusIcon => GetStatusIcon();
    public string StatusIconDescription => GetStatusIconDescription();
    public string StatusText => GetStatusText();
    public Color StatusTextColor => GetStatusTextColor();
    public string DetailText => GetDetailText();
    public Color DetailTextColor => GetDetailTextColor();
    public bool HasDetailText => !string.IsNullOrEmpty(DetailText);
    public string ActionText => GetActionText();
    public bool HasAction => !string.IsNullOrEmpty(ActionText);
    public string ActionDescription => GetActionDescription();

    #endregion

    #region Events

    public event EventHandler? ActionTapped;

    #endregion

    public ConnectionStatusBanner()
    {
        InitializeComponent();
    }

    #region Property Change Handlers

    private static void OnConnectionStateChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ConnectionStatusBanner banner)
        {
            banner.UpdateAllDisplayProperties();
        }
    }

    private static void OnDisplayPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is ConnectionStatusBanner banner)
        {
            banner.UpdateAllDisplayProperties();
        }
    }

    private void UpdateAllDisplayProperties()
    {
        OnPropertyChanged(nameof(BannerBackgroundColor));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(StatusIconDescription));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTextColor));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(DetailTextColor));
        OnPropertyChanged(nameof(HasDetailText));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(HasAction));
        OnPropertyChanged(nameof(ActionDescription));
    }

    #endregion

    #region Display Logic

    private Color GetBannerBackgroundColor()
    {
        if (!IsBluetoothEnabled)
        {
            return GetColorResource("WarningLight", "WarningLightDark");
        }

        return ConnectionState switch
        {
            ConnectionState.Connected => GetColorResource("SuccessLight", "SuccessLightDark"),
            ConnectionState.Connecting => GetColorResource("InfoLight", "InfoLightDark"),
            ConnectionState.Disconnected => GetColorResource("Gray100", "Gray600"),
            ConnectionState.Error => GetColorResource("DangerLight", "DangerLightDark"),
            _ => GetColorResource("Gray100", "Gray600")
        };
    }

    private string GetStatusIcon()
    {
        if (!IsBluetoothEnabled)
        {
            return "⚠️"; // Warning for BT disabled
        }

        return ConnectionState switch
        {
            ConnectionState.Connected => "✓",
            ConnectionState.Connecting => "", // ActivityIndicator shown instead
            ConnectionState.Disconnected => "📵",
            ConnectionState.Error => "⚠️",
            _ => "📵"
        };
    }

    private string GetStatusIconDescription()
    {
        if (!IsBluetoothEnabled)
        {
            return "Warning icon, Bluetooth is disabled";
        }

        return ConnectionState switch
        {
            ConnectionState.Connected => "Checkmark icon, device connected",
            ConnectionState.Connecting => "Loading indicator, connecting to device",
            ConnectionState.Disconnected => "Disconnected icon, no device connected",
            ConnectionState.Error => "Warning icon, connection error",
            _ => "Status unknown"
        };
    }

    private string GetStatusText()
    {
        if (!IsBluetoothEnabled)
        {
            return "Bluetooth Disabled";
        }

        return ConnectionState switch
        {
            ConnectionState.Connected => !string.IsNullOrEmpty(DeviceName) ? DeviceName : "Connected",
            ConnectionState.Connecting => "Connecting...",
            ConnectionState.Disconnected => "No Device Connected",
            ConnectionState.Error => "Connection Lost",
            _ => "Unknown Status"
        };
    }

    private Color GetStatusTextColor()
    {
        if (!IsBluetoothEnabled)
        {
            return GetColorResource("WarningLightDark", "WarningDark");
        }

        return ConnectionState switch
        {
            ConnectionState.Connected => GetColorResource("SuccessLightDark", "SuccessDark"),
            ConnectionState.Connecting => GetColorResource("InfoLightDark", "InfoDark"),
            ConnectionState.Disconnected => GetColorResource("Gray600", "Gray400"),
            ConnectionState.Error => GetColorResource("DangerLightDark", "DangerDark"),
            _ => GetColorResource("Gray600", "Gray400")
        };
    }

    private string GetDetailText()
    {
        if (ConnectionState == ConnectionState.Connected && BatteryLevel.HasValue)
        {
            return $"Battery: {BatteryLevel}%";
        }

        return string.Empty;
    }

    private Color GetDetailTextColor()
    {
        // Slightly muted version of status text color
        return GetColorResource("Gray500", "Gray400");
    }

    private string GetActionText()
    {
        if (!IsBluetoothEnabled)
        {
            return "Enable";
        }

        return ConnectionState switch
        {
            ConnectionState.Disconnected => "Find Device",
            ConnectionState.Error => "Reconnect",
            _ => string.Empty
        };
    }

    private string GetActionDescription()
    {
        if (!IsBluetoothEnabled)
        {
            return "Open device settings to enable Bluetooth";
        }

        return ConnectionState switch
        {
            ConnectionState.Disconnected => "Navigate to device list to find and connect a BlueBuzzah glove",
            ConnectionState.Error => "Attempt to reconnect to the BlueBuzzah glove",
            _ => string.Empty
        };
    }

    private Color GetColorResource(string lightKey, string darkKey)
    {
        // Dark-mode-only app - always use the dark key (BlueBuzzah.com design)
        if (Application.Current?.Resources.TryGetValue(darkKey, out var colorValue) == true && colorValue is Color color)
        {
            return color;
        }

        // Fallback color for dark mode
        return Colors.DarkGray;
    }

    #endregion

    #region Event Handlers

    private async void OnActionTapped(object? sender, EventArgs e)
    {
        ActionTapped?.Invoke(this, EventArgs.Empty);

        // Default action handling if no external handler
        if (!IsBluetoothEnabled)
        {
            // Open device settings (platform-specific)
            await OpenBluetoothSettings();
        }
        else if (ConnectionState == ConnectionState.Disconnected)
        {
            // Navigate to Devices tab
            await Shell.Current.GoToAsync("//devices");
        }
        else if (ConnectionState == ConnectionState.Error)
        {
            // Navigate to Devices tab for reconnection
            await Shell.Current.GoToAsync("//devices");
        }
    }

    private async Task OpenBluetoothSettings()
    {
        try
        {
#if ANDROID
            var intent = new Android.Content.Intent(Android.Provider.Settings.ActionBluetoothSettings);
            intent.SetFlags(Android.Content.ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(intent);
#elif IOS || MACCATALYST
            var url = new Foundation.NSUrl("App-Prefs:root=Bluetooth");
            if (UIKit.UIApplication.SharedApplication.CanOpenUrl(url))
            {
                await UIKit.UIApplication.SharedApplication.OpenUrlAsync(url, new UIKit.UIApplicationOpenUrlOptions());
            }
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConnectionStatusBanner] Failed to open Bluetooth settings: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    #endregion
}
