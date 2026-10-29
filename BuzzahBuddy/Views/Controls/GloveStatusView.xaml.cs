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
            var low = GloveStatusText.IsLow(percent);
            BatteryGlyph.Text = GloveStatusText.BatteryGlyph(percent);
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
