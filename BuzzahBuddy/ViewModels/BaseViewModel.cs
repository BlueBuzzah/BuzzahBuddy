using CommunityToolkit.Mvvm.ComponentModel;

namespace BuzzahBuddy.ViewModels;

// TODO (.NET 10): Migrate Shell.Current.DisplayAlert/DisplayActionSheet to *Async variants.
// 46 calls across 5 ViewModels. Consider extracting IDialogService for testability.

/// <summary>
/// Base class for all ViewModels in the application.
/// Provides common properties and functionality for MVVM pattern.
/// Implements IDisposable to allow proper cleanup of event subscriptions.
/// </summary>
public partial class BaseViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// Gets a value indicating whether the ViewModel is not busy.
    /// Useful for enabling/disabling UI elements.
    /// </summary>
    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
    }

    /// <summary>
    /// Releases all resources used by the ViewModel.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources. Override in derived classes to unsubscribe from events.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Derived classes should override to unsubscribe from events
            }
            _disposed = true;
        }
    }
}
