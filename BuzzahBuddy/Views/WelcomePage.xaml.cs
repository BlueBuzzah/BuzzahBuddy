using BuzzahBuddy.Helpers;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace BuzzahBuddy.Views;

public partial class WelcomePage : ContentPage
{
    /// <summary>Preferences key set once the person has read the notices and continued.</summary>
    public const string AcceptedKey = "WelcomeNoticesAccepted";

    public WelcomePage()
    {
        InitializeComponent();
        // Full screen, not iOS's swipe-to-dismiss card: the notices can't be swiped away.
        On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.FullScreen);
    }

    protected override bool OnBackButtonPressed() => true;

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        Preferences.Default.Set(AcceptedKey, true);
        await Navigation.PopModalAsync(animated: !Motion.Reduce);
    }
}
