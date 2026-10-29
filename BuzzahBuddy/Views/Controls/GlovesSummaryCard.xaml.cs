using System.Windows.Input;
using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;

namespace BuzzahBuddy.Views.Controls;

/// <summary>Both gloves in one tappable card; see <see cref="GloveStatusText.Summary"/> for the words.</summary>
public partial class GlovesSummaryCard : ContentView
{
    public static readonly BindableProperty PrimaryProperty = BindableProperty.Create(
        nameof(Primary), typeof(GloveStatusInfo), typeof(GlovesSummaryCard), null,
        propertyChanged: (b, _, _) => ((GlovesSummaryCard)b).Apply());

    public static readonly BindableProperty SecondaryProperty = BindableProperty.Create(
        nameof(Secondary), typeof(GloveStatusInfo), typeof(GlovesSummaryCard), null,
        propertyChanged: (b, _, _) => ((GlovesSummaryCard)b).Apply());

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(GlovesSummaryCard));

    public GlovesSummaryCard()
    {
        InitializeComponent();
        SemanticProperties.SetHint(Root, "Opens the Gloves tab");
        Apply();
    }

    public GloveStatusInfo? Primary { get => (GloveStatusInfo?)GetValue(PrimaryProperty); set => SetValue(PrimaryProperty, value); }

    public GloveStatusInfo? Secondary { get => (GloveStatusInfo?)GetValue(SecondaryProperty); set => SetValue(SecondaryProperty, value); }

    /// <summary>Runs on tap; the Session screen opens the Gloves tab.</summary>
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    private void Apply()
    {
        var primary = Primary ?? new GloveStatusInfo(GloveRole.Primary, GloveLinkStatus.Disconnected, null);
        var secondary = Secondary ?? new GloveStatusInfo(GloveRole.Secondary, GloveLinkStatus.Disconnected, null);
        var (tone, title, detail) = GloveStatusText.Summary(primary, secondary);
        var (ground, ink, glyph) = StatusToneStyle.For(tone);
        IconGround.SetDynamicResource(BackgroundColorProperty, ground);
        GlyphLabel.SetDynamicResource(Label.TextColorProperty, ink);
        GlyphLabel.Text = glyph ?? LucideIcons.Hand;
        TitleText.Text = title;
        DetailText.Text = detail;
        DetailRow.IsVisible = detail is not null;
        var lowest = new[] { primary.BatteryPercent, secondary.BatteryPercent }.Min();
        BatteryGlyph.IsVisible = lowest is not null;
        if (lowest is { } percent)
        {
            BatteryGlyph.Text = GloveStatusText.BatteryGlyph(percent);
            BatteryGlyph.SetDynamicResource(Label.TextColorProperty, GloveStatusText.IsLow(percent) ? "Warning" : "InkMuted");
        }
        SemanticProperties.SetDescription(Root, $"{GloveStatusText.Describe(primary)}. {GloveStatusText.Describe(secondary)}");
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (Command?.CanExecute(null) == true)
            Command.Execute(null);
        // Visible acknowledgment for tremor (as on the Gloves profile cards); none under reduced motion.
        if (Motion.Reduce)
            return;
        var fast = (uint)(int)Application.Current!.Resources["DurationFast"];
        await Root.ScaleToAsync(0.98, fast, Motion.Standard);
        await Root.ScaleToAsync(1.0, fast, Motion.Standard);
    }
}
