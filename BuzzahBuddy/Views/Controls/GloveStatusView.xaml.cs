using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;

namespace BuzzahBuddy.Views.Controls;

/// <summary>Summarizes one glove; see <see cref="GloveStatusText"/> for the words.</summary>
public partial class GloveStatusView : ContentView
{
    public static readonly BindableProperty InfoProperty = BindableProperty.Create(
        nameof(Info), typeof(GloveStatusInfo), typeof(GloveStatusView), null,
        propertyChanged: (b, _, _) => ((GloveStatusView)b).Apply());

    public GloveStatusView()
    {
        InitializeComponent();
        HandGlyph.Text = LucideIcons.Hand;
        Apply();
        this.TrackTextScale(UpdateLayout);
    }

    /// <summary>Large text: the hand above the name, and the voltage on its own line.</summary>
    private void UpdateLayout()
    {
        var stacked = OsAccessibility.StackForLargeText;
        var twoRows = new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto));
        NameRow.RowDefinitions = stacked ? twoRows : new RowDefinitionCollection(new RowDefinition(GridLength.Auto));
        Grid.SetRow(NameText, stacked ? 1 : 0);
        Grid.SetColumn(NameText, stacked ? 0 : 1);
        Grid.SetColumnSpan(NameText, stacked ? 2 : 1);

        BatteryRow.RowDefinitions = stacked
            ? new RowDefinitionCollection(new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto))
            : new RowDefinitionCollection(new RowDefinition(GridLength.Auto));
        Grid.SetRow(DetailText, stacked ? 1 : 0);
        Grid.SetColumn(DetailText, stacked ? 0 : 2);
        Grid.SetColumnSpan(DetailText, stacked ? 3 : 1);
        DetailText.HorizontalOptions = stacked ? LayoutOptions.Start : LayoutOptions.End;
    }

    public GloveStatusInfo? Info
    {
        get => (GloveStatusInfo?)GetValue(InfoProperty);
        set => SetValue(InfoProperty, value);
    }

    private void Apply()
    {
        var info = Info ?? new GloveStatusInfo(GloveRole.Primary, GloveLinkStatus.Disconnected, null);
        NameText.Text = GloveStatusText.Name(info.Role);
        (Badge.Tone, Badge.Text) = GloveStatusText.Badge(info.Status);
        Badge.Glyph = info.Status == GloveLinkStatus.Connecting ? LucideIcons.Bluetooth : null;

        var showBattery = info.Status == GloveLinkStatus.Connected && info.BatteryPercent is not null;
        BatteryRow.IsVisible = showBattery;
        if (info.BatteryPercent is { } percent)
        {
            var low = info.BatteryLow;
            BatteryGlyph.Text = GloveStatusText.BatteryGlyph(percent, low);
            BatteryGlyph.SetDynamicResource(Label.TextColorProperty, low ? "Warning" : "InkMuted");
            PercentText.Text = $"{percent}%";
            PercentText.SetDynamicResource(Label.TextColorProperty, low ? "Warning" : "Ink");
            LowText.IsVisible = low;
        }

        DetailText.Text = info.Detail;
        DetailText.IsVisible = info.Detail is not null;
        SemanticProperties.SetDescription(Root, GloveStatusText.Describe(info));
    }
}
