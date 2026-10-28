using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class ProfileSettingsPage : ContentPage
{
    private readonly ProfileSettingsViewModel _viewModel;

    public ProfileSettingsPage(ProfileSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        Unloaded += (_, _) => _viewModel.Dispose();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
