using Microsoft.Maui.Controls.Shapes;

namespace BuzzahBuddy.Views.Controls;

/// <summary>
/// A still bar for the History average chart (ProgressBar README): a 12-tall
/// <c>SurfaceSunken</c> track with a <c>LineStrong</c> edge and a <c>Brand</c> fill. Decorative
/// for screen readers; the row it sits in carries the spoken text.
/// </summary>
public sealed class MeterBar : ContentView
{
    public static readonly BindableProperty FractionProperty = BindableProperty.Create(
        nameof(Fraction), typeof(double), typeof(MeterBar), 0.0, propertyChanged: (b, _, _) => ((MeterBar)b).Apply());

    private readonly Grid _columns = new();

    public MeterBar()
    {
        var fill = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
        };
        fill.SetDynamicResource(BackgroundColorProperty, "Brand");
        _columns.Add(fill);

        var track = new Border
        {
            HeightRequest = 12,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = _columns,
        };
        track.SetDynamicResource(BackgroundColorProperty, "SurfaceSunken");
        track.SetDynamicResource(Border.StrokeProperty, "LineStrong");
        track.SetDynamicResource(Border.StrokeThicknessProperty, "BorderHairline");

        Content = track;
        AutomationProperties.SetIsInAccessibleTree(this, false);
        Apply();
    }

    /// <summary>How full the bar is, 0 to 1.</summary>
    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    private void Apply()
    {
        var f = Math.Clamp(Fraction, 0, 1);
        _columns.ColumnDefinitions = new ColumnDefinitionCollection(
            new ColumnDefinition(new GridLength(f, GridUnitType.Star)),
            new ColumnDefinition(new GridLength(1 - f, GridUnitType.Star)));
    }
}
