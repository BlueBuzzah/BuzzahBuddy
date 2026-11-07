using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;

namespace BuzzahBuddy.Views;

public partial class WelcomePage : ContentPage
{
    /// <summary>Preferences key holding the Terms effective date the person agreed to.</summary>
    public const string TermsAcceptedDateKey = "TermsAcceptedDate";

    /// <summary>Pre-Terms bool key. Cleared on the first agreement; its holders see Welcome once more.</summary>
    public const string LegacyAcceptedKey = "WelcomeNoticesAccepted";

    public WelcomePage()
    {
        InitializeComponent();
        // Full screen, not iOS's swipe-to-dismiss card: the notices can't be swiped away.
        On<iOS>().SetModalPresentationStyle(UIModalPresentationStyle.FullScreen);
        // I Agree stays inside the column, so it lines up with the notices above it.
        this.TrackLayout(layout =>
        {
            Column.ApplyColumn(layout);
            AgreeButton.ApplyColumn(layout);
        });
    }

    protected override bool OnBackButtonPressed() => true;

    private async void OnTermsClicked(object? sender, EventArgs e) =>
        await Browser.Default.OpenAsync(LegalNotices.TermsUrl, BrowserLaunchMode.SystemPreferred);

    private async void OnPrivacyClicked(object? sender, EventArgs e) =>
        await Browser.Default.OpenAsync(LegalNotices.PrivacyUrl, BrowserLaunchMode.SystemPreferred);

    private async void OnAgreeClicked(object? sender, EventArgs e)
    {
        Preferences.Default.Set(TermsAcceptedDateKey, LegalNotices.TermsEffectiveDate);
        Preferences.Default.Remove(LegacyAcceptedKey);
        await Navigation.PopModalAsync(animated: !Motion.Reduce);
    }
}
