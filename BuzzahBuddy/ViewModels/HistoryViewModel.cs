using System.Collections.ObjectModel;
using System.Globalization;
using BuzzahBuddy.Helpers;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Bluetooth;
using BuzzahBuddy.Services.Glove;
using BuzzahBuddy.Services.Reports;
using BuzzahBuddy.Services.Storage;
using BuzzahBuddy.Views.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuzzahBuddy.ViewModels;

/// <summary>
/// The History tab (buzzahbuddy.md History): daily totals, the last 7 days, day notes and the
/// shared report. Reloads each time the page appears; never subscribes to service events.
/// </summary>
public partial class HistoryViewModel : BaseViewModel
{
	private readonly HistoryStore _history;

	/// <summary>A tremor can double-fire the tap; one share sheet is enough.</summary>
	private readonly RepeatGuard _shareGuard = new(RepeatGuard.SessionControlWindow);

	[ObservableProperty]
	private IReadOnlyList<DayRow> _days = [];

	[ObservableProperty]
	private IReadOnlyList<WeekBar> _lastSevenDays = [];

	[ObservableProperty]
	private int _selectedRange = (int)ReportRange.Last90Days;

	[ObservableProperty]
	private bool _hasSessions;

	[ObservableProperty]
	private bool _isDemoMode;

	/// <summary>30 Days / 90 Days / All Time, valued as <see cref="ReportRange"/>.</summary>
	public IList<SegmentOption> RangeOptions { get; } = new[]
	{
		new SegmentOption((int)ReportRange.Last30Days, "30 Days"),
		new SegmentOption((int)ReportRange.Last90Days, "90 Days"),
		new SegmentOption((int)ReportRange.AllTime, "All Time"),
	};

	public HistoryViewModel(HistoryStore history)
	{
		_history = history;
		Title = "History";
	}

	private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

	[RelayCommand]
	private async Task RefreshAsync()
	{
		IsDemoMode = Preferences.Default.Get(SwitchableBluetoothService.DemoModeKey, false);
		HistoryData data;
		try
		{
			data = await _history.LoadAsync();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			await ShowErrorAsync(new UserError(
				"Can't Load Your History",
				"Something went wrong reading your history. Try again.",
				ex.Message));
			return;
		}

		var today = Today;
		var culture = CultureInfo.CurrentCulture;
		// An emptied note is "", which shows nothing, like no note.
		var rows = HistoryText.DayRows(HistorySummary.Days(data.Sessions, data.Notes, today), today, culture)
			.Select(r => string.IsNullOrEmpty(r.Note) ? r with { Note = null } : r)
			.ToList();
		// A new list scrolls the page back to the top, so after a note edit (or a tab switch)
		// the same days are updated in place and the person keeps their place.
		if (Days is ObservableCollection<DayRow> current && current.Select(r => r.Day).SequenceEqual(rows.Select(r => r.Day)))
		{
			for (var i = 0; i < rows.Count; i++)
			{
				if (!SameRow(current[i], rows[i]))
					current[i] = rows[i];
			}
		}
		else
		{
			Days = new ObservableCollection<DayRow>(rows);
		}

		var bars = HistoryText.WeekBars(HistorySummary.LastDays(data.Sessions, today, 7), culture);
		if (!LastSevenDays.SequenceEqual(bars))
			LastSevenDays = bars;
		HasSessions = data.Sessions.Count > 0;
	}

	// DayRow's SessionLines list compares by reference; compare its contents instead.
	private static bool SameRow(DayRow a, DayRow b) =>
		a with { SessionLines = b.SessionLines } == b && a.SessionLines.SequenceEqual(b.SessionLines);

	[RelayCommand]
	private async Task EditNoteAsync(DayRow row)
	{
		var result = await Shell.Current.DisplayPromptAsync(
			$"Note for {row.Title}",
			"Write how you felt or anything you want to remember. It stays on this phone.",
			accept: "Save Note",
			cancel: "Cancel",
			placeholder: "For example: Tremor felt lighter after lunch",
			maxLength: HistoryStore.MaxNoteLength,
			keyboard: Keyboard.Text,
			initialValue: row.Note ?? "");
		if (result is null)
			return;

		var removed = string.IsNullOrWhiteSpace(result);
		// Saving an empty note on a day without one changes nothing, so don't say it was removed.
		if (removed && row.Note is null)
			return;

		try
		{
			await _history.SetDayNoteAsync(row.Day, result);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			await ShowErrorAsync(new UserError(
				"Can't Save the Note",
				"Something went wrong saving your note. Try again.",
				ex.Message));
			return;
		}

		await RefreshAsync();
		SemanticScreenReader.Announce(removed ? "Note removed" : "Note saved");
	}

	[RelayCommand]
	private async Task ShareReportAsync()
	{
		if (!_shareGuard.TryAcquire())
			return;

		// The async command already disables Share Report while it runs; IsBusy marks the render.
		IsBusy = true;
		try
		{
			var data = await _history.LoadAsync();
			var (from, to) = HistoryReport.Range((ReportRange)SelectedRange, Today, data.Sessions);
			var cache = FileSystem.CacheDirectory;
			// Earlier reports hold health notes; keep only the one being shared.
			foreach (var old in Directory.EnumerateFiles(cache, "BuzzahBuddy Report *")
				.Concat(Directory.EnumerateFiles(cache, "BuzzahBuddy Sessions *")).ToList())
			{
				// A file that won't go is left for next time; it doesn't stop this report.
				try
				{
					File.Delete(old);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					System.Diagnostics.Debug.WriteLine($"[HISTORY] Couldn't delete old report {Path.GetFileName(old)}: {ex.Message}");
				}
			}

			var model = HistoryReport.Build(data, from, to, DateTimeOffset.Now);
			var bytes = await Task.Run(() =>
				ReportPdf.Render(model, ReportAssets.FromEmbedded(typeof(App).Assembly)));

			var stamp = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			var pdf = Path.Combine(cache, $"BuzzahBuddy Report {stamp}.pdf");
			var csv = Path.Combine(cache, $"BuzzahBuddy Sessions {stamp}.csv");
			await File.WriteAllBytesAsync(pdf, bytes);
			await File.WriteAllTextAsync(csv, HistoryReport.Csv(HistoryReport.InRange(data.Sessions, from, to)));
			IsBusy = false;

			await Share.Default.RequestAsync(new ShareMultipleFilesRequest
			{
				Title = "BuzzahBuddy Report",
				Files = [new ShareFile(pdf, "application/pdf"), new ShareFile(csv, "text/csv")],
			});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[HISTORY] Report failed: {ex.Message}");
			await ShowErrorAsync(new UserError(
				"Can't Make the Report",
				"Something went wrong writing the report. Try again.",
				ex.Message));
		}
		finally
		{
			IsBusy = false;
		}
	}

	private static Task ShowErrorAsync(UserError error) =>
		Shell.Current.DisplayAlertAsync(error.Title, error.ForAlert, "OK");
}
