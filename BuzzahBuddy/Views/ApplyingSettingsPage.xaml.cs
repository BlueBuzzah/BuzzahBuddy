using BuzzahBuddy.ViewModels;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace BuzzahBuddy.Views;

/// <summary>
/// Blocking modal shown while the gloves reboot and auto-reconnect after
/// Apply Settings. Pushed/popped by DeviceSettingsViewModel's apply watch.
/// </summary>
public partial class ApplyingSettingsPage : ContentPage
{
    public ApplyingSettingsPage(DeviceSettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;

        // Full screen on iOS so the sheet can't be swipe-dismissed mid-apply.
        On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.FullScreen);
    }

    // Block Android hardware/gesture back while applying.
    protected override bool OnBackButtonPressed() => true;
}
