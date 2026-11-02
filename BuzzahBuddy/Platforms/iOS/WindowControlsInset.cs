using CoreGraphics;
using UIKit;

namespace BuzzahBuddy;

/// <summary>
/// iPadOS 26+ draws a window's close, minimize and resize controls over its top-leading corner,
/// outside the safe area, so a tab screen's headline sat under them. A page that starts at the
/// window's top edge with its nav bar hidden (tab screens and full-screen modals) gets the extra
/// top inset that clears the controls, so its headline still sits directly below the safe area.
/// A page that shows its nav bar gets nothing: the bar already makes room. iPhone and full-screen
/// iPad windows have no controls, so the inset is 0.
/// </summary>
static class WindowControlsInset
{
	public static void Register() =>
		Microsoft.Maui.Handlers.PageHandler.Mapper.AppendToMapping(nameof(WindowControlsInset), (_, view) =>
		{
			if (!OperatingSystem.IsIOSVersionAtLeast(26) || view is not Page page)
				return;
			// Switching between a window and full screen resizes the page.
			page.SizeChanged -= OnSizeChanged;
			page.SizeChanged += OnSizeChanged;
		});

	static void OnSizeChanged(object? sender, EventArgs e)
	{
		if (!OperatingSystem.IsIOSVersionAtLeast(26)
			|| sender is not Page { Handler: IPlatformViewHandler { ViewController: { } controller } } page
			|| controller.View is not { Window: { } window } view)
			return;

		// Measured on the window: it never gets this extra inset, so it can't feed back, and it knows
		// about the controls at once (the views inside it catch up a layout pass or two later).
		var adapted = window.GetEdgeInsets(
			UIViewLayoutRegion.CreateSafeAreaLayoutRegion(UIViewLayoutRegionAdaptivityAxis.Vertical));
		// Only pages that hide the nav bar and start at the window's top edge draw under the controls.
		var underControls = !Shell.GetNavBarIsVisible(page) && view.ConvertPointToView(CGPoint.Empty, null).Y < 1;
		nfloat extra = underControls ? nfloat.Max(0, adapted.Top - window.SafeAreaInsets.Top) : 0;

		if (nfloat.Abs(controller.AdditionalSafeAreaInsets.Top - extra) > 0.5)
			controller.AdditionalSafeAreaInsets = new UIEdgeInsets(extra, 0, 0, 0);
	}
}
