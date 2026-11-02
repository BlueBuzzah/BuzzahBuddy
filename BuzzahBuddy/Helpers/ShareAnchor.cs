namespace BuzzahBuddy.Helpers;

/// <summary>Where the iPad share popover should point: the control that asked for the share.</summary>
public static class ShareAnchor
{
    /// <summary>
    /// The anchor's bounds in window coordinates, read now so they match the current scroll
    /// position. <see cref="Rect.Zero"/> when there is no anchor, it isn't on screen, or the
    /// platform has no popover (Android).
    /// </summary>
    public static Rect Bounds(VisualElement? anchor)
    {
#if IOS || MACCATALYST
        if (anchor?.Handler?.PlatformView is UIKit.UIView view && view.Window is not null)
        {
            var rect = view.ConvertRectToView(view.Bounds, null);
            return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }
#endif
        return Rect.Zero;
    }
}
