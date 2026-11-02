using System.Globalization;

namespace BuzzahBuddy.Models;

/// <summary>One day cell of the history calendar. <see cref="OutsidePeriod"/>: before the period's first day, or after today.</summary>
public sealed record CalendarDay(DateOnly Day, string Number, double Fraction, bool HasTime, bool IsToday, bool OutsidePeriod, string Spoken);

/// <summary>A month grid: weekday headers, blanks before the 1st, then every day.</summary>
public sealed record CalendarMonth(DateOnly First, string Title, IReadOnlyList<string> WeekdayNames, int LeadingBlanks, IReadOnlyList<CalendarDay> Days);

/// <summary>Builds the month calendar and steps between months.</summary>
public static class HistoryCalendar
{
    /// <summary>The month containing <paramref name="month"/>; any day in it will do. Days before <paramref name="from"/>, where the period's calendar starts, are outside it.</summary>
    public static CalendarMonth Build(IEnumerable<SessionRecord> sessions, DateOnly month, DateOnly from, DateOnly today, CultureInfo culture)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var format = culture.DateTimeFormat;
        var firstDay = (int)format.FirstDayOfWeek;

        var secondsByDay = sessions
            .GroupBy(s => s.Day)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TherapySeconds));

        var weekdays = Enumerable.Range(0, 7).Select(i => format.AbbreviatedDayNames[(firstDay + i) % 7]).ToList();
        var days = Enumerable.Range(0, DateTime.DaysInMonth(first.Year, first.Month))
            .Select(i => CalendarDayFor(first.AddDays(i), secondsByDay, from, today, culture))
            .ToList();

        return new CalendarMonth(
            first,
            first.ToString("MMMM yyyy", culture),
            weekdays,
            ((int)first.DayOfWeek - firstDay + 7) % 7,
            days);
    }

    /// <summary>The first of the month <paramref name="months"/> from <paramref name="shown"/>, kept between <paramref name="first"/> and <paramref name="last"/>.</summary>
    public static DateOnly Step(DateOnly shown, int months, DateOnly first, DateOnly last)
    {
        var target = new DateOnly(shown.Year, shown.Month, 1).AddMonths(months);
        return target < first ? first : target > last ? last : target;
    }

    private static CalendarDay CalendarDayFor(DateOnly day, Dictionary<DateOnly, int> secondsByDay, DateOnly from, DateOnly today, CultureInfo culture)
    {
        var seconds = secondsByDay.GetValueOrDefault(day);
        var isToday = day == today;
        var hasTime = seconds > 0;
        var date = day.ToString("dddd, MMMM d", culture);
        var spoken = hasTime
            ? isToday ? $"{date}, today, {DurationText.Spoken(seconds)}" : $"{date}, {DurationText.Spoken(seconds)}"
            : isToday ? $"{date}, today, no sessions yet" : $"{date}, no sessions";

        return new CalendarDay(
            day,
            day.Day.ToString(culture),
            HistorySummary.BarFraction(seconds),
            hasTime,
            isToday,
            day < from || day > today,
            spoken);
    }
}
