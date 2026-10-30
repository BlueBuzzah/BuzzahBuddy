using System.Globalization;

namespace BuzzahBuddy.Models;

/// <summary>Time as people read it (design system README "Voice"; bundle.js clock() and spoken()).</summary>
public static class DurationText
{
    /// <summary>The ring's countdown: "1:42:07", or "5:00" under an hour.</summary>
    public static string Clock(int seconds)
    {
        seconds = Math.Max(0, seconds);
        int h = seconds / 3600, m = seconds % 3600 / 60, s = seconds % 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00}");
    }

    /// <summary>For screen readers: "1 hour 42 minutes". Minutes round half up, as JS Math.round does.</summary>
    public static string Spoken(int seconds)
    {
        seconds = Math.Max(0, seconds);
        var h = seconds / 3600;
        var m = (int)Math.Round(seconds % 3600 / 60.0, MidpointRounding.AwayFromZero);
        var parts = new List<string>();
        if (h > 0)
            parts.Add(h == 1 ? "1 hour" : $"{h} hours");
        if (m > 0 || h == 0)
            parts.Add(m == 1 ? "1 minute" : $"{m} minutes");
        return string.Join(" ", parts);
    }

    /// <summary>Compact duration for labels: "2h", "1h 30min", "5min".</summary>
    public static string Short(int seconds)
    {
        seconds = Math.Max(0, seconds);
        int h = seconds / 3600, m = seconds % 3600 / 60;
        return (h, m) switch
        {
            (0, _) => $"{m}min",
            (_, 0) => $"{h}h",
            _ => $"{h}h {m}min",
        };
    }
}
