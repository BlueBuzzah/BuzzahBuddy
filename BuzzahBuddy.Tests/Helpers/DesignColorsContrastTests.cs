using BuzzahBuddy.Helpers;
using Microsoft.Maui.Graphics;
using Xunit;

namespace BuzzahBuddy.Tests.Helpers;

/// <summary>
/// Checks the generated design-system colors meet the contrast floors tokens.json
/// promises for each theme. A failure here is a design-system defect to report,
/// not a value to patch in the app.
/// </summary>
public class DesignColorsContrastTests
{
    public static TheoryData<string> Themes => new() { "light", "dark", "light-hc", "dark-hc" };

    private static readonly string[] Surfaces = { "Surface", "SurfaceRaised", "SurfaceSunken", "SurfaceOverlay" };

    private static Color C(string theme, string key) => DesignColors.Themes[theme][key];

    private static double TextFloor(string theme) => theme.EndsWith("-hc") ? 7.0 : 4.5;

    private static double Luminance(Color c)
    {
        static double Channel(float v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(c.Red) + 0.7152 * Channel(c.Green) + 0.0722 * Channel(c.Blue);
    }

    private static double Ratio(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static void AssertAtLeast(string theme, string fg, string bg, double floor) =>
        Assert.True(Ratio(C(theme, fg), C(theme, bg)) >= floor,
            $"{theme}: {fg} on {bg} is {Ratio(C(theme, fg), C(theme, bg)):F2}:1, needs {floor}:1");

    [Theory, MemberData(nameof(Themes))]
    public void Ink_and_InkMuted_on_every_surface(string theme)
    {
        foreach (var bg in Surfaces)
        {
            AssertAtLeast(theme, "Ink", bg, TextFloor(theme));
            AssertAtLeast(theme, "InkMuted", bg, TextFloor(theme));
        }
    }

    [Theory, MemberData(nameof(Themes))]
    public void Brand_text_on_grounds(string theme)
    {
        foreach (var bg in new[] { "Surface", "SurfaceRaised", "SurfaceSunken", "BrandSoft" })
            AssertAtLeast(theme, "Brand", bg, 4.5);
        AssertAtLeast(theme, "OnBrand", "Brand", 4.5);
    }

    [Theory, MemberData(nameof(Themes))]
    public void Status_text_on_grounds(string theme)
    {
        foreach (var tone in new[] { "Success", "Warning", "Danger" })
        {
            AssertAtLeast(theme, tone, "Surface", 4.5);
            AssertAtLeast(theme, tone, tone + "Soft", 4.5);
        }
        AssertAtLeast(theme, "Neutral", "NeutralSoft", 4.5);
    }

    [Theory, MemberData(nameof(Themes))]
    public void Control_edges(string theme)
    {
        foreach (var bg in new[] { "Surface", "SurfaceRaised", "SurfaceSunken" })
            AssertAtLeast(theme, "LineStrong", bg, 3.0);
        foreach (var bg in new[] { "Surface", "SurfaceRaised" })
            AssertAtLeast(theme, "Focus", bg, 3.0);
    }
}
