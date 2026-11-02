using UIKit;

namespace BuzzahBuddy;

/// <summary>
/// iPadOS 26+ draws a window's close, minimize and resize controls over its top-leading corner,
/// outside the safe area, so a tab screen's headline sat under them. A page with its nav bar
/// hidden (tab screens and full-screen modals, which all start at the window's top edge) gets the
/// extra top inset that clears the controls, so its headline still sits directly below the safe area.
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
			// Loaded comes once the view is in its window (a modal's first SizeChanged can come before);
			// switching between a window and full screen resizes the page.
			page.Loaded -= Update;
			page.Loaded += Update;
			page.SizeChanged -= Update;
			page.SizeChanged += Update;
		});

	static void Update(object? sender, EventArgs e)
	{
		if (!OperatingSystem.IsIOSVersionAtLeast(26)
			|| sender is not Page { Handler: IPlatformViewHandler { ViewController: { } controller } } page
			|| controller.View?.Window is not { } window)
			return;

		// Measured on the window: it never gets this extra inset, so it can't feed back, and it knows
		// about the controls at once (the views inside it catch up a layout pass or two later).
		var adapted = window.GetEdgeInsets(
			UIViewLayoutRegion.CreateSafeAreaLayoutRegion(UIViewLayoutRegionAdaptivityAxis.Vertical));
		// Not a frame test: a modal mid-presentation is still below the top edge.
		nfloat extra = !Shell.GetNavBarIsVisible(page) ? nfloat.Max(0, adapted.Top - window.SafeAreaInsets.Top) : 0;

		if (nfloat.Abs(controller.AdditionalSafeAreaInsets.Top - extra) > 0.5)
			controller.AdditionalSafeAreaInsets = new UIEdgeInsets(extra, 0, 0, 0);
	}
}
