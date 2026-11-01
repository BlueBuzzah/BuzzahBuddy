using BuzzahBuddy.Models;

namespace BuzzahBuddy.Helpers;

/// <summary>Keeps a page's layout in step with its window size (rotation, Split View, resizing).</summary>
public static class LayoutTracking
{
    /// <summary>
    /// Calls <paramref name="update"/> with the page's <see cref="PageLayout"/> whenever it changes: on
    /// each size change, and when the window resumes (the OS text size can change in the background).
    /// Subscribes on Loaded and unsubscribes on Unloaded, so it suits tab pages (never disposed) and
    /// pushed or modal pages alike. Never reports a zero-size layout.
    /// </summary>
    public static void TrackLayout(this ContentPage page, Action<PageLayout> update)
    {
        PageLayout? last = null;
        Window? window = null;

        void Evaluate()
        {
            if (page.Width <= 0 || page.Height <= 0)
                return;
            var layout = AdaptiveLayout.For(page.Width, page.Height, OsAccessibility.TextScale);
            if (layout == last)
                return;
            last = layout;
            update(layout);
        }

        void OnSizeChanged(object? sender, EventArgs e) => Evaluate();
        void OnResumed(object? sender, EventArgs e) => Evaluate();

        page.Loaded += (_, _) =>
        {
            page.SizeChanged += OnSizeChanged;
            window = page.Window;
            if (window is not null)
                window.Resumed += OnResumed;
            Evaluate();
        };
        page.Unloaded += (_, _) =>
        {
            page.SizeChanged -= OnSizeChanged;
            if (window is not null)
                window.Resumed -= OnResumed;
            window = null;
        };
    }

    /// <summary>
    /// Sizes <paramref name="column"/> to the layout's column width, centered, and gives it the
    /// layout's gutter as its own horizontal margin (its top and bottom margin are kept), so the gutter
    /// lives in one place and pages drop their own horizontal padding. The scroll view stays full
    /// window width. It sets WidthRequest, not Fill plus MaximumWidthRequest: on iOS the latter
    /// measures wrapping labels at the wider width and clips them (checked on the 13" iPad).
    /// </summary>
    public static void ApplyColumn(this View column, PageLayout layout)
    {
        column.WidthRequest = layout.ColumnWidth;
        column.HorizontalOptions = LayoutOptions.Center;
        var m = column.Margin;
        column.Margin = new Thickness(layout.Gutter, m.Top, layout.Gutter, m.Bottom);
    }
}
