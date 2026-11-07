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

    private async void OnTermsClicked(object? sender, EventArgs e) =>
        await Browser.Default.OpenAsync(LegalNotices.TermsUrl, BrowserLaunchMode.SystemPreferred);

    private async void OnPrivacyClicked(object? sender, EventArgs e) =>
        await Browser.Default.OpenAsync(LegalNotices.PrivacyUrl, BrowserLaunchMode.SystemPreferred);
}
