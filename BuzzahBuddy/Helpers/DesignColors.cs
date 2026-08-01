// Explicit for the plain net10.0 test build, which lacks MAUI implicit usings
using Microsoft.Maui.Graphics;

namespace BuzzahBuddy.Helpers;

/// <summary>
/// Design token values as compile-time constants (see DESIGN_GUIDE.md, "Color and Theme
/// Guidelines", for the rules these must satisfy).
/// Single source for hex values used from C# — either directly by code that cannot
/// reach <c>Application.Current.Resources</c> (plain net10.0 test build), or as the
/// fallback for <c>ColorResources.Get</c>. Must stay in sync with Colors.xaml.
/// </summary>
public static class DesignColors
{
    public static readonly Color Primary = Color.FromArgb("#35B6F2");
    public static readonly Color PrimaryDarkText = Color.FromArgb("#0a0a0a");
    public static readonly Color Secondary = Color.FromArgb("#05212D");
    public static readonly Color CardBorder = Color.FromArgb("#0A3143");
    public static readonly Color Warning = Color.FromArgb("#f59e0b");
    public static readonly Color DangerDark = Color.FromArgb("#fb7185");
}
