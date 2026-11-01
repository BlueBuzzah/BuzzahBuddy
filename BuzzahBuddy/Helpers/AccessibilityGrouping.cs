namespace BuzzahBuddy.Helpers;

public static class AccessibilityGrouping
{
    /// <summary>
    /// Makes VoiceOver read this view's children as one group, so side-by-side panes are read
    /// pane by pane, not interleaved row by row. iOS only: TalkBack already walks a ViewGroup
    /// depth-first, so Android needs nothing.
    /// </summary>
    public static void GroupForScreenReader(this View view)
    {
#if IOS
        void Apply()
        {
            if (view.Handler?.PlatformView is UIKit.UIView native)
                native.ShouldGroupAccessibilityChildren = true;
        }
        view.HandlerChanged += (_, _) => Apply();
        Apply();
#endif
    }
}
