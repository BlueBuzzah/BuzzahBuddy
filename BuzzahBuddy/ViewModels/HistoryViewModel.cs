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
/// The History tab (buzzahbuddy.md History): the period overview, the month calendar, weekly or
/// monthly averages, the shared report, and daily totals with notes. Reloads each time the page
/// appears; never subscribes to service events. The period and month recompute from the loaded data.
/// </summary>
public partial class HistoryViewModel : BaseViewModel
{
	private readonly HistoryStore _history;

	/// <summary>A tremor can double-fire the tap; one share sheet is enough.</summary>
	private readonly RepeatGuard _shareGuard = new(RepeatGuard.SessionControlWindow);

	[ObservableProperty]
	private IReadOnlyList<DayRow> _days = [];

	[ObservableProperty]
	private int _selectedRange = (int)ReportRange.Last30Days;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowCalendar))]
	private bool _hasSessions;

	/// <summary>Total Time, Days Used, Average Day, Sessions; always four, so the page's index bindings resolve.</summary>
	[ObservableProperty]
	private IReadOnlyList<StatTile> _tiles = HistoryText.Tiles(
		HistoryReport.Build(new HistoryData(), Today, Today, DateTimeOffset.Now, comparePrevious: false),
		ReportRange.Last30Days, CultureInfo.CurrentCulture);

	[ObservableProperty]
	private IReadOnlyList<BarRow> _trendRows = [];

	[ObservableProperty]
	private string _trendTitle = HistoryText.TrendTitle(weekly: true);

	[ObservableProperty]
	private string _trendCaption = HistoryText.TrendCaption(weekly: true, capped: false);

	[ObservableProperty]
	private string _shareText = HistoryText.ShareText(ReportRange.Last30Days);

	/// <summary>"No sessions in the last 30 days." when there is history but none in the period; otherwise null.</summary>
	[ObservableProperty]
	private string? _emptyPeriodText;

	[ObservableProperty]
	private bool _hasPeriodSessions;

	[ObservableProperty]
	private CalendarMonth? _calendar;

	/// <summary>Set by the page at the large-text threshold, where the calendar steps aside.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowCalendar))]
	private bool _largeText;

	private HistoryData? _data;

	/// <summary>Whether the loaded data is Demo Mode's sample; null before the first load.</summary>
	private bool? _loadedDemo;

	/// <summary>Raised after a refresh that swapped the sample for the person's own history, or back.</summary>
	public event EventHandler? DataSourceChanged;

	/// <summary>The calendar's month (its first day); kept across refreshes while the app runs.</summary>
	private DateOnly _shownMonth = FirstOfMonth(Today);

	/// <summary>The current month at the last compute, to tell when a new month has begun.</summary>
	private DateOnly _currentMonth = FirstOfMonth(Today);

	/// <summary>Seven columns can't hold day numbers at large text; the rows below carry the same time.</summary>
	public bool ShowCalendar => HasSessions && !LargeText;

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
		// Applied only once the load succeeds, so a failed load never leaves the other data on screen
		// under the wrong banner.
		var demo = Preferences.Default.Get(SwitchableBluetoothService.DemoModeKey, false);
		HistoryData data;
		try
		{
			data = await LoadAsync(demo);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			await ShowErrorAsync(new UserError(
				"Can't Load Your History",
				"Something went wrong reading your history. Try again.",
				ex.Message));
			return;
		}

		IsDemoMode = demo;
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

		HasSessions = data.Sessions.Count > 0;
		_data = data;
		Recompute();

		// Other history is a different page, so it starts at the top.
		var flipped = _loadedDemo is { } wasDemo && wasDemo != IsDemoMode;
		_loadedDemo = IsDemoMode;
		if (flipped)
			DataSourceChanged?.Invoke(this, EventArgs.Empty);
	}

	partial void OnSelectedRangeChanged(int value)
	{
		if (_data is null)
			return;
		Recompute();
		// The tiles change out of view of the period control; say the new total, as month changes do.
		SemanticScreenReader.Announce(Tiles[0].Spoken);
	}

	/// <summary>The overview, averages and calendar from the loaded data, without rereading the file.</summary>
	private void Recompute()
	{
		if (_data is not { } data)
			return;

		var today = Today;
		var culture = CultureInfo.CurrentCulture;
		var range = (ReportRange)SelectedRange;
		var (from, to) = HistoryReport.Range(range, today, data.Sessions);
		var model = HistoryReport.Build(data, from, to, DateTimeOffset.Now, comparePrevious: false);
		var weekly = HistoryText.IsWeekly(model);

		HasPeriodSessions = model.Summary.Sessions > 0;
		// Any change to the list header moves the page on iOS, so a refresh that finds the same
		// numbers leaves the bound lists and the calendar untouched (as Days does above).
		var tiles = HistoryText.Tiles(model, range, culture);
		if (!Tiles.SequenceEqual(tiles))
			Tiles = tiles;
		var rows = HistoryText.TrendRows(model, culture);
		if (!TrendRows.SequenceEqual(rows))
			TrendRows = rows;
		TrendTitle = HistoryText.TrendTitle(weekly);
		TrendCaption = HistoryText.TrendCaption(weekly, capped: !weekly && model.Trend.Count > HistoryText.MonthlyRowLimit);
		ShareText = HistoryText.ShareText(range);
		EmptyPeriodText = HasSessions && !HasPeriodSessions ? HistoryText.EmptyPeriodText(range) : null;

		// Showing the current month follows it into the next one after midnight on the 1st;
		// a month paged back to stays put.
		var currentMonth = FirstOfMonth(today);
		if (_shownMonth == _currentMonth)
			_shownMonth = currentMonth;
		_currentMonth = currentMonth;
		_shownMonth = HistoryCalendar.Step(_shownMonth, 0, HistoryCalendar.FirstMonth(data.Sessions, today), currentMonth);
		var month = HistoryCalendar.Build(data.Sessions, _shownMonth, today, culture);
		if (!SameMonth(Calendar, month))
			Calendar = month;
		PreviousMonthCommand.NotifyCanExecuteChanged();
		NextMonthCommand.NotifyCanExecuteChanged();
	}

	private bool CanShowPreviousMonth() =>
		_data is not null && _shownMonth > HistoryCalendar.FirstMonth(_data.Sessions, Today);

	private bool CanShowNextMonth() => _shownMonth < FirstOfMonth(Today);

	[RelayCommand(CanExecute = nameof(CanShowPreviousMonth))]
	private void PreviousMonth() => ShowMonth(-1);

	[RelayCommand(CanExecute = nameof(CanShowNextMonth))]
	private void NextMonth() => ShowMonth(1);

	private void ShowMonth(int months)
	{
		if (_data is not { } data)
			return;
		var today = Today;
		// A month paged to after midnight on the 1st is the person's choice, not one to follow.
		_currentMonth = FirstOfMonth(today);
		_shownMonth = HistoryCalendar.Step(_shownMonth, months, HistoryCalendar.FirstMonth(data.Sessions, today), _currentMonth);
		Calendar = HistoryCalendar.Build(data.Sessions, _shownMonth, today, CultureInfo.CurrentCulture);
		SemanticScreenReader.Announce(Calendar.Title);
		PreviousMonthCommand.NotifyCanExecuteChanged();
		NextMonthCommand.NotifyCanExecuteChanged();
	}

	private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);

	// CalendarMonth's lists compare by reference; compare their contents instead.
	private static bool SameMonth(CalendarMonth? a, CalendarMonth b) =>
		a is not null
		&& a with { WeekdayNames = b.WeekdayNames, Days = b.Days } == b
		&& a.WeekdayNames.SequenceEqual(b.WeekdayNames)
		&& a.Days.SequenceEqual(b.Days);

	// DayRow's SessionLines list compares by reference; compare its contents instead.
	private static bool SameRow(DayRow a, DayRow b) =>
		a with { SessionLines = b.SessionLines } == b && a.SessionLines.SequenceEqual(b.SessionLines);

	[RelayCommand]
	private async Task EditNoteAsync(DayRow row)
	{
		// Demo Mode's days are sample data; a note would land in the person's own history.
		if (IsDemoMode)
			return;

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
			var sample = IsDemoMode;
			var data = await LoadAsync(sample);
			var range = (ReportRange)SelectedRange;
			var (from, to) = HistoryReport.Range(range, Today, data.Sessions);
			var cache = FileSystem.CacheDirectory;
			// Earlier reports hold health notes; keep only the one being shared.
			foreach (var old in HistoryReport.OldReportPatterns
				.SelectMany(pattern => Directory.EnumerateFiles(cache, pattern)).ToList())
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

			var model = HistoryReport.Build(data, from, to, DateTimeOffset.Now, comparePrevious: range != ReportRange.AllTime, sample);
			var bytes = await Task.Run(() =>
				ReportPdf.Render(model, ReportAssets.FromEmbedded(typeof(App).Assembly)));

			var stamp = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			var names = HistoryReport.FileNames(stamp, sample);
			var pdf = Path.Combine(cache, names.Pdf);
			var csv = Path.Combine(cache, names.Csv);
			await File.WriteAllBytesAsync(pdf, bytes);
			await File.WriteAllTextAsync(csv, HistoryReport.Csv(
				HistoryReport.InRange(data.Sessions, from, to),
				data.Notes.Where(n => n.Day >= from && n.Day <= to)));
			IsBusy = false;

			await Share.Default.RequestAsync(new ShareMultipleFilesRequest
			{
				Title = sample ? "BuzzahBuddy Sample Report" : "BuzzahBuddy Report",
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

	/// <summary>Demo Mode's sample history, never stored, or the person's own.</summary>
	private Task<HistoryData> LoadAsync(bool demo) => demo
		? Task.FromResult(DemoHistory.Build(DateTimeOffset.Now))
		: _history.LoadAsync();

	private static Task ShowErrorAsync(UserError error) =>
		Shell.Current.DisplayAlertAsync(error.Title, error.ForAlert, "OK");
}
