using System.ComponentModel;
using BuzzahBuddy.ViewModels;

namespace BuzzahBuddy.Views;

public partial class CalibrationPage : ContentPage
{
    private readonly CalibrationViewModel _viewModel;

    public CalibrationPage(CalibrationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += OnPageUnloaded;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Move screen-reader focus to the step heading when the wizard advances
        if (e.PropertyName == nameof(CalibrationViewModel.CurrentStep))
            StepTitleLabel.SetSemanticFocus();
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.Dispose();
    }
}
