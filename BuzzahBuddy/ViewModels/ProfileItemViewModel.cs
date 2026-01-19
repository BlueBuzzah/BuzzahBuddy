using BuzzahBuddy.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// Wrapper ViewModel for TherapyProfile to handle selection state.
/// </summary>
public partial class ProfileItemViewModel : ObservableObject
{
    // Primary profiles shown by default (Noisy, Regular, Quick Test)
    private static readonly int[] PrimaryProfileIds = { 2, 1, 6 };

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

    /// <summary>
    /// Whether this profile is recommended (Noisy is the default recommended profile).
    /// </summary>
    public bool IsRecommended => ProfileId == 2;

    /// <summary>
    /// Display name including "(Recommended)" suffix if applicable.
    /// </summary>
    public string DisplayName => IsRecommended ? $"{Name} (Recommended)" : Name;

    /// <summary>
    /// Whether this is a primary profile (shown by default).
    /// Primary: Noisy, Regular, Quick Test
    /// </summary>
    public bool IsPrimaryProfile => PrimaryProfileIds.Contains(ProfileId);

    /// <summary>
    /// Whether this is an advanced profile (shown in "More" section).
    /// Advanced: Hybrid, Custom, Gentle
    /// </summary>
    public bool IsAdvancedProfile => !IsPrimaryProfile;
}
