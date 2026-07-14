using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class DeviceListPage : ContentPage
{
    private readonly DeviceListViewModel _viewModel;

    public DeviceListPage(DeviceListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnPageAppearing();
    }

}
