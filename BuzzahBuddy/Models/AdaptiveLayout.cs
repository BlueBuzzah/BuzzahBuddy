namespace BuzzahBuddy.Models;

/// <summary>What a page needs to lay itself out at its current window size.</summary>
/// <param name="Width">Window width in points.</param>
/// <param name="Height">Window height in points.</param>
/// <param name="TwoPane">True when the text-relative width has room for two panes.</param>
/// <param name="ColumnWidth">Width of the centered content column.</param>
/// <param name="Gutter">Horizontal space between the window edge and the column.</param>
public readonly record struct PageLayout(double Width, double Height, bool TwoPane, double ColumnWidth, double Gutter)
{
    public bool Landscape => Width > Height;
}

/// <summary>
/// The width model: laid out by window size, never device type. Larger OS text counts against the
/// width, so large text on a wide window gets the single column already proven at large text.
/// </summary>
public static class AdaptiveLayout
{
    /// <summary>Text-relative window width (width / max(1, text scale)) at which two panes fit.</summary>
    public const double TwoPaneMinWidth = 900;

    /// <summary>Window width at which the gutter grows from 16 to 24.</summary>
    public const double WideGutterMinWidth = 600;

    /// <summary>Prose width (42rem) at default text size; it scales with the OS text size like rem.</summary>
    public const double ColumnMaxWidth = 672;

    public static PageLayout For(double width, double height, double textScale)
    {
        var scale = Math.Max(1, textScale);
        var gutter = width >= WideGutterMinWidth ? 24 : 16;
        var column = Math.Max(0, Math.Min(width - 2 * gutter, ColumnMaxWidth * scale));
        return new PageLayout(width, height, width / scale >= TwoPaneMinWidth, column, gutter);
    }
}
