using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        this.TrackLayout(layout => Column.ApplyColumn(layout));
    }

    // SafeFireAndForget: Android throws when no browser is installed, which would crash an async void handler.
    private void OnTermsClicked(object? sender, EventArgs e) =>
        Browser.Default.OpenAsync(LegalNotices.TermsUrl, BrowserLaunchMode.SystemPreferred).SafeFireAndForget("[SETTINGS]");

    private void OnPrivacyClicked(object? sender, EventArgs e) =>
        Browser.Default.OpenAsync(LegalNotices.PrivacyUrl, BrowserLaunchMode.SystemPreferred).SafeFireAndForget("[SETTINGS]");
}
