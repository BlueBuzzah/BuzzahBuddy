using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;

namespace BuzzahBuddy.Views.Controls;

/// <summary>Two GloveStatus cards: two columns, or one column when the OS text size is large.</summary>
public partial class GloveStatusPair : ContentView
{
    public static readonly BindableProperty PrimaryProperty = BindableProperty.Create(
        nameof(Primary), typeof(GloveStatusInfo), typeof(GloveStatusPair), null,
        propertyChanged: (b, _, n) => ((GloveStatusPair)b).PrimaryView.Info = (GloveStatusInfo?)n);

    public static readonly BindableProperty SecondaryProperty = BindableProperty.Create(
        nameof(Secondary), typeof(GloveStatusInfo), typeof(GloveStatusPair), null,
        propertyChanged: (b, _, n) => ((GloveStatusPair)b).SecondaryView.Info = (GloveStatusInfo?)n);

    public GloveStatusPair()
    {
        InitializeComponent();
        SecondaryView.Info = new GloveStatusInfo(GloveRole.Secondary, GloveLinkStatus.Disconnected, null);
        this.TrackTextScale(UpdateLayout);
    }

    public GloveStatusInfo? Primary { get => (GloveStatusInfo?)GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }

    public GloveStatusInfo? Secondary { get => (GloveStatusInfo?)GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }

    private void UpdateLayout()
    {
        var stacked = OsAccessibility.StackForLargeText;
        Cards.ColumnDefinitions = stacked
            ? new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star))
            : new ColumnDefinitionCollection(new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star));
        Cards.RowDefinitions = stacked
            ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto))
            : new RowDefinitionCollection(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(SecondaryView, stacked ? 0 : 1);
        Grid.SetRow(SecondaryView, stacked ? 1 : 0);
    }
}
