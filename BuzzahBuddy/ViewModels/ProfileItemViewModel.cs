using BuzzahBuddy.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// Wrapper ViewModel for TherapyProfile to handle selection state.
/// </summary>
public partial class ProfileItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public TherapyProfile Profile { get; }

    public ProfileItemViewModel(TherapyProfile profile)
    {
        Profile = profile;
    }

    // Expose profile properties for binding
    public int ProfileId => Profile.ProfileId;
    public string Name => Profile.Name;
    public string Description => Profile.Description;
}
