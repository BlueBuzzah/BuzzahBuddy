namespace BuzzahBuddy.Models;

/// <summary>How big the session ring is drawn, from the space it has rather than the screen.</summary>
public static class SessionRingSize
{
    /// <summary>
    /// In one column (<paramref name="paneHeight"/> null), <paramref name="width"/> is the window
    /// width: 240 under 360, 280 under 600, else 360. In its own pane, width and height are the
    /// pane's: the smaller side less 48, kept between 360 and 480.
    /// </summary>
    public static double For(double width, double? paneHeight)
    {
        if (paneHeight is { } height)
            return Math.Clamp(Math.Min(width, height) - 48, 360, 480);
        return width < 360 ? 240 : width < 600 ? 280 : 360;
    }
}
