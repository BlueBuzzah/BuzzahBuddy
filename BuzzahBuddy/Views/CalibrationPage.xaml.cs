using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class CalibrationPage : ContentPage
{
    public CalibrationPage(CalibrationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
