using System.Globalization;
using System.Reflection;
using System.Text;
using BuzzahBuddy.Models;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace BuzzahBuddy.Services.Reports;

/// <summary>The report's fonts and the BlueBuzzah mark. The app embeds them; tests read the same files from disk.</summary>
/// <param name="LoadFont">Font file bytes for a face name such as "Lexend-Medium".</param>
/// <param name="BrandMarkPng">The design system's mark, unmodified.</param>
public sealed record ReportAssets(Func<string, byte[]> LoadFont, byte[] BrandMarkPng)
{
    private const string ResourcePrefix = "BuzzahBuddy.Report.";
    private const string MarkFile = "bluebuzzah-mark.png";

    /// <summary>The files <see cref="FromEmbedded"/> reads, each embedded as <c>BuzzahBuddy.Report.{file}</c>.</summary>
    public static IReadOnlyList<string> ResourceFiles { get; } =
    [
        "Lexend-Medium.ttf",
        "Lexend-SemiBold.ttf",
        "Lexend-Bold.ttf",
        "AtkinsonHyperlegibleNext-Regular.ttf",
        "AtkinsonHyperlegibleNext-Bold.ttf",
        MarkFile,
    ];

    /// <summary>Reads every file up front, so a missing resource fails before any drawing.</summary>
    public static ReportAssets FromEmbedded(Assembly assembly)
    {
        var files = ResourceFiles.ToDictionary(f => f, f => Read(assembly, f));
        return new ReportAssets(face => files[face + ".ttf"], files[MarkFile]);
    }

    private static byte[] Read(Assembly assembly, string file)
    {
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + file)
            ?? throw new InvalidOperationException($"Missing report resource {ResourcePrefix}{file}");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

/// <summary>
/// Draws the shared session report as two fixed US Letter pages in the design system's light theme,
/// with PDFsharp XGraphics. Page 1 shows whether the gloves are used; page 2, how. Every session is
/// in the CSV, so the PDF has no tables. Holds no MAUI types.
/// </summary>
public static class ReportPdf
{
    public const string Title = "BuzzahBuddy Session Report";

    // Font families the resolver maps to the app's TTFs.
    private const string Display = "Lexend";
    private const string DisplaySemiBold = "Lexend SemiBold";
    private const string Body = "Atkinson Hyperlegible Next";

    // Light theme tokens.
    private static readonly XColor Ink = XColor.FromArgb(0x0b, 0x16, 0x30);
    private static readonly XColor InkMuted = XColor.FromArgb(0x47, 0x55, 0x70);
    private static readonly XColor Brand = XColor.FromArgb(0x00, 0x59, 0xd6);
    private static readonly XColor Surface = XColor.FromArgb(0xf6, 0xf8, 0xfc);
    private static readonly XColor SurfaceSunken = XColor.FromArgb(0xeb, 0xef, 0xf6);
    private static readonly XColor Line = XColor.FromArgb(0xd6, 0xde, 0xea);

    /// <summary>The calendar's five steps of <c>brand</c> over white: none, under 1 h, 1 to 2 h, 2 to 3 h, 3 h and over.</summary>
    private static readonly XColor[] Ramp =
    [
        SurfaceSunken,
        XColor.FromArgb(0xc9, 0xd9, 0xf5),
        XColor.FromArgb(0x8d, 0xb1, 0xec),
        XColor.FromArgb(0x3f, 0x7f, 0xe0),
        Brand,
    ];

    // US Letter, 0.75 in margins.
    private const double PageWidth = 612;
    private const double PageHeight = 792;
    private const double Margin = 54;
    private const double ContentWidth = PageWidth - 2 * Margin;
    private const double Right = PageWidth - Margin;

    private const double MarkHeight = 18;
    // Lexend's cap height at 11.5 pt is about 45% of the mark, as the design system's lockup asks.
    private const double WordmarkSize = 11.5;
    private const double RuleY = Margin + MarkHeight + 8;
    private const double BodyTop = RuleY + 22;
    private const double FooterBottom = PageHeight - Margin;
    internal const double BodyBottom = FooterBottom - 24;

    private const int PageCount = 2;
    private const string DateFormat = "MMM d, yyyy";
    private const string ShortDateFormat = "MMM d";

    /// <summary>Longest run of non-space characters set in a note before a space is inserted.</summary>
    internal const int MaxWordLength = 40;

    private const double SectionGap = 18;
    private const double TrendHeight = 150;
    private const double MinTrendHeight = 110;

    internal const string TrendCaption =
        "Average time per day. Days without a session count as zero. The dashed line is the schedule early studies used (Pfeifer et al. 2021).";

    private const string AboutNumbers =
        "Times are what BuzzahBuddy recorded on this phone. Paused time isn't counted. Sessions your gloves ran while this phone wasn't connected may be missing.";

    private static readonly object FontGate = new();
    private static bool _fontsReady;
    private static HashSet<int> _bodyGlyphs = [];

    /// <summary>
    /// The report's copy is English, so every date and time is en-US whatever the phone's culture:
    /// the fonts are Latin-only, and other calendars and scripts would garble or mix formats.
    /// </summary>
    internal static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>The PDF, the lines of text drawn on each page, and where page content ends, for tests.</summary>
    internal sealed record Layout(byte[] Pdf, IReadOnlyList<IReadOnlyList<string>> PageText, double Page1Bottom, double NotesBottom, double LegalTop, IReadOnlyList<Box> Boxes);

    /// <summary>Where page 2 set a run of user text or the bar beside it, for overlap tests. <paramref name="Row"/> pairs runs on one line or row.</summary>
    internal sealed record Box(string Kind, double Row, XRect Rect);

    /// <summary>The PDF for <paramref name="model"/>.</summary>
    public static byte[] Render(ReportModel model, ReportAssets assets) => Draw(model, assets).Pdf;

    internal static Layout Draw(ReportModel model, ReportAssets assets)
    {
        EnsureFonts(assets);

        using var pdf = new PdfDocument();
        pdf.Info.Title = Title;
        pdf.Info.Subject = $"{model.From.ToString(DateFormat, Culture)} to {model.To.ToString(DateFormat, Culture)}";
        pdf.Info.Author = "BuzzahBuddy";
        pdf.Info.Creator = "BuzzahBuddy";
        pdf.Options.CompressContentStreams = true;

        using var markStream = new MemoryStream(assets.BrandMarkPng);
        using var mark = XImage.FromStream(markStream);

        double page1Bottom, notesBottom, legalTop;
        var page1 = new Canvas(XGraphics.FromPdfPage(AddPage(pdf)));
        using (page1.G)
        {
            DrawHeader(page1, mark);
            page1Bottom = DrawPage1(page1, model);
            DrawFooter(page1, 1);
        }

        var page2 = new Canvas(XGraphics.FromPdfPage(AddPage(pdf)));
        using (page2.G)
        {
            DrawHeader(page2, mark);
            (notesBottom, legalTop) = DrawPage2(page2, model);
            DrawFooter(page2, 2);
        }

        using var output = new MemoryStream();
        pdf.Save(output, false);
        return new Layout(output.ToArray(), [page1.Lines, page2.Lines], page1Bottom, notesBottom, legalTop, page2.Boxes);
    }

    /// <summary>The font resolver can be set once per process, so the first assets given serve every report.</summary>
    private static void EnsureFonts(ReportAssets assets)
    {
        lock (FontGate)
        {
            if (_fontsReady)
                return;
            GlobalFontSettings.FontResolver = new ReportFontResolver(assets.LoadFont);
            GlobalFontSettings.DefaultFontEncoding = PdfFontEncoding.Unicode;
            _bodyGlyphs = FontCmap.CodePoints(assets.LoadFont("AtkinsonHyperlegibleNext-Regular"));
            _fontsReady = true;
        }
    }

    private static PdfPage AddPage(PdfDocument pdf)
    {
        var page = pdf.AddPage();
        page.Size = PageSize.Letter;
        return page;
    }

    /// <summary>A page's graphics plus each line of text drawn on it, so tests can read the page.</summary>
    private sealed class Canvas(XGraphics g)
    {
        public XGraphics G { get; } = g;
        public List<string> Lines { get; } = [];
        public List<Box> Boxes { get; } = [];

        public void Text(string text, XFont font, XBrush brush, XRect box, XStringFormat format)
        {
            Lines.Add(text);
            G.DrawString(text, font, brush, box, format);
        }

        /// <summary>Draws one line with its top at <paramref name="top"/>; returns the top of the next line.</summary>
        public double Line(string text, XFont font, XBrush brush, double top, double x = Margin)
        {
            Text(text, font, brush, new XRect(x, top, Right - x, font.GetHeight()), XStringFormats.TopLeft);
            return top + font.GetHeight();
        }

        public XSize Measure(string text, XFont font) => G.MeasureString(text, font);
    }

    // ---- Page furniture ----

    private static void DrawHeader(Canvas c, XImage mark)
    {
        var markWidth = MarkHeight * mark.PixelWidth / mark.PixelHeight;
        c.G.DrawImage(mark, Margin, Margin, markWidth, MarkHeight);
        var x = Margin + markWidth + 6;
        c.Text("BlueBuzzah", Font(DisplaySemiBold, WordmarkSize), new XSolidBrush(Ink),
            new XRect(x, Margin, Right - x, MarkHeight), XStringFormats.CenterLeft);
        c.Text("Session Report", Font(Body, 9), new XSolidBrush(InkMuted),
            new XRect(Margin, Margin, ContentWidth, MarkHeight), XStringFormats.CenterRight);
        c.G.DrawLine(new XPen(Line, 0.75), Margin, RuleY, Right, RuleY);
    }

    private static void DrawFooter(Canvas c, int page)
    {
        var font = Font(Body, 8);
        var brush = new XSolidBrush(InkMuted);
        var box = new XRect(Margin, FooterBottom - font.GetHeight(), ContentWidth, font.GetHeight());
        c.Text("BuzzahBuddy", font, brush, box, XStringFormats.BottomLeft);
        c.Text($"Page {page} of {PageCount}", font, brush, box, XStringFormats.BottomRight);
    }

    private static double SectionTitle(Canvas c, string title, double top, double x = Margin) =>
        c.Line(title, Font(DisplaySemiBold, 13), new XSolidBrush(Ink), top, x) + 6;

    // ---- Page 1: are they using it? ----

    /// <summary>Draws page 1; returns the y where its content ends.</summary>
    private static double DrawPage1(Canvas c, ReportModel model)
    {
        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(InkMuted);
        var sum = model.Summary;
        var y = BodyTop;

        y = c.Line("Session Report", Font(Display, 24, bold: true), ink, y);
        y += 2;
        y = c.Line(
            $"{model.From.ToString(DateFormat, Culture)} to {model.To.ToString(DateFormat, Culture)} · {Count(model.To.DayNumber - model.From.DayNumber + 1, "day")}",
            Font(Body, 11), muted, y);
        y += 1;
        y = c.Line($"Created {model.CreatedAt.ToString(DateFormat, Culture)}", Font(Body, 9), muted, y);
        y += 12;

        y = DrawLede(c, model.Lede, y);
        y += 12;

        var prev = model.Previous;
        string? Previous(string value) => prev is null ? null : $"Previous {prev.DaysInPeriod} days: {value}";
        y = DrawTiles(c, y,
        [
            new Tile("Days With Sessions", $"{sum.DaysWithSessions} of {sum.DaysInPeriod}", Previous(prev?.DaysWithSessions.ToString(Culture) ?? "")),
            new Tile("Average per Day Used", DurationText.Short(sum.AverageSecondsPerSessionDay), Previous(DurationText.Short(prev?.AverageSecondsPerSessionDay ?? 0))),
            new Tile("Total Time", DurationText.Short(sum.TherapySeconds), Previous(DurationText.Short(prev?.TherapySeconds ?? 0))),
            new Tile("Sessions", sum.Sessions.ToString(Culture),
                sum.DaysWithSessions == 0 ? null : $"{((double)sum.Sessions / sum.DaysWithSessions).ToString("0.0", Culture)} a day when used"),
        ]);
        y += SectionGap;

        y = SectionTitle(c, "Daily Use", y);
        // The trend section below keeps 150 pt of chart; the calendar takes what's left, and only
        // if a calendar can't fit at a readable size does the chart give up height (to 110 pt).
        var trendFixed = TrendSectionHeight(c);
        var sinceFirst = model.AdherenceFrom > model.From;
        var legendHeight = (sinceFirst ? 2 : 1) * Font(Body, 8).GetHeight() + 4;
        var calendarRoom = BodyBottom - y - legendHeight - SectionGap - trendFixed - TrendHeight;
        var calendarNeeds = CalendarMinHeight(model);
        var chartHeight = TrendHeight;
        if (calendarNeeds > calendarRoom)
        {
            chartHeight = Math.Max(MinTrendHeight, TrendHeight - (calendarNeeds - calendarRoom));
            calendarRoom += TrendHeight - chartHeight;
        }

        y = model.Calendar == CalendarStyle.MonthGrids
            ? DrawMonthGrids(c, model.Days, model.AdherenceFrom, y, calendarRoom)
            : DrawMonthRows(c, model.Days, model.AdherenceFrom, y, calendarRoom);
        y = DrawCalendarLegend(c, y + 4, model.Days.Count > 0 && model.Days[0].Day > model.From);
        // Days before the first session ever are drawn as outside the period; say why.
        if (sinceFirst)
            y = c.Line($"First session {model.AdherenceFrom.ToString(DateFormat, Culture)}.", Font(Body, 8), muted, y, Right - c.Measure($"First session {model.AdherenceFrom.ToString(DateFormat, Culture)}.", Font(Body, 8)).Width);
        y += SectionGap;

        y = SectionTitle(c, model.TrendTitle, y);
        y = DrawTrend(c, model, y + 4, chartHeight);
        y += 4;
        foreach (var line in Wrap(c, TrendCaption, Font(Body, 9), ContentWidth))
            y = c.Line(line, Font(Body, 9), muted, y);
        return y;
    }

    /// <summary>The trend section's height apart from the plot: title, gap, date labels and caption.</summary>
    private static double TrendSectionHeight(Canvas c) =>
        Font(DisplaySemiBold, 13).GetHeight() + 6 + 4 + 4 + Font(Body, 8).GetHeight() + 4
        + Wrap(c, TrendCaption, Font(Body, 9), ContentWidth).Count * Font(Body, 9).GetHeight();

    private static double DrawLede(Canvas c, IReadOnlyList<string> lede, double top)
    {
        const double padX = 12, padY = 9;
        var font = Font(Body, 10.5);
        var lines = Wrap(c, string.Join(" ", lede), font, ContentWidth - 2 * padX);
        var height = 2 * padY + lines.Count * font.GetHeight();
        c.G.DrawRoundedRectangle(new XSolidBrush(Surface), Margin, top, ContentWidth, height, 12, 12);
        var y = top + padY;
        foreach (var line in lines)
            y = c.Line(line, font, new XSolidBrush(Ink), y, Margin + padX);
        return top + height;
    }

    private sealed record Tile(string Label, string Value, string? Detail);

    private static double DrawTiles(Canvas c, double top, IReadOnlyList<Tile> tiles)
    {
        const double gap = 10, padding = 10;
        var width = (ContentWidth - gap * (tiles.Count - 1)) / tiles.Count;
        var inner = width - 2 * padding;
        var labelFont = Font(Body, 9);
        var detailFont = Font(Body, 8.5);

        // One value size for every tile: 18 pt, or smaller if a value wouldn't fit its tile.
        var valueSize = 18.0;
        foreach (var t in tiles)
        {
            var w = c.Measure(t.Value, Font(DisplaySemiBold, 18)).Width;
            if (w > inner)
                valueSize = Math.Min(valueSize, Math.Floor(18 * inner / w * 2) / 2);
        }
        var valueFont = Font(DisplaySemiBold, valueSize);

        var labels = tiles.Select(t => Wrap(c, t.Label, labelFont, inner)).ToList();
        // A detail too long for one line breaks after its colon, keeping the value whole.
        List<string> DetailLines(string? d)
        {
            if (d is null)
                return [];
            var colon = d.IndexOf(": ", StringComparison.Ordinal);
            return c.Measure(d, detailFont).Width <= inner || colon < 0 ? Wrap(c, d, detailFont, inner) : [d[..(colon + 1)], d[(colon + 2)..]];
        }
        var details = tiles.Select(t => DetailLines(t.Detail)).ToList();
        var labelHeight = labels.Max(l => l.Count) * labelFont.GetHeight();
        var detailHeight = details.Max(d => d.Count) * detailFont.GetHeight();
        var height = padding + labelHeight + 2 + valueFont.GetHeight() + (detailHeight > 0 ? 1 + detailHeight : 0) + padding;

        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(InkMuted);
        for (var i = 0; i < tiles.Count; i++)
        {
            var x = Margin + i * (width + gap);
            c.G.DrawRoundedRectangle(new XSolidBrush(SurfaceSunken), x, top, width, height, 12, 12);
            var y = top + padding;
            foreach (var line in labels[i])
                y = c.Line(line, labelFont, muted, y, x + padding);
            // Values share one baseline even when a label wraps.
            y = top + padding + labelHeight + 2;
            y = c.Line(tiles[i].Value, valueFont, ink, y, x + padding);
            y += 1;
            foreach (var line in details[i])
                y = c.Line(line, detailFont, muted, y, x + padding);
        }
        return top + height;
    }

    // ---- Daily Use calendar ----

    private static XColor Shade(int seconds) => seconds switch
    {
        <= 0 => Ramp[0],
        < 3600 => Ramp[1],
        < 7200 => Ramp[2],
        < 10800 => Ramp[3],
        _ => Ramp[4],
    };

    /// <summary>Day numbers: white on the two darkest steps; ink on the middle steps; ink-muted on an empty day.</summary>
    private static XColor DayNumberColor(int seconds) => seconds switch
    {
        <= 0 => InkMuted,
        < 7200 => Ink,
        _ => XColors.White,
    };

    private const int MaxGridMonths = 4;
    private const double GridGap = 16;
    private const double MaxGridPitch = (ContentWidth - (MaxGridMonths - 1) * GridGap) / MaxGridMonths / 7;
    private const double MinGridPitch = 13;

    private static List<(int Year, int Month)> Months(IReadOnlyList<DayCell> days)
    {
        var months = new List<(int, int)>();
        if (days.Count == 0)
            return months;
        for (var m = new DateOnly(days[0].Day.Year, days[0].Day.Month, 1); m <= days[^1].Day; m = m.AddMonths(1))
            months.Add((m.Year, m.Month));
        return months;
    }

    private static int WeekRows(int year, int month)
    {
        var lead = ((int)new DateOnly(year, month, 1).DayOfWeek + 6) % 7;
        return (lead + DateTime.DaysInMonth(year, month) + 6) / 7;
    }

    /// <summary>The smallest height the calendar can take and stay readable.</summary>
    private static double CalendarMinHeight(ReportModel model)
    {
        if (model.Calendar == CalendarStyle.MonthGrids)
        {
            var rows = Months(model.Days).Select(m => WeekRows(m.Year, m.Month)).DefaultIfEmpty(5).Max();
            return GridHeaderHeight() + rows * MinGridPitch;
        }
        var count = Months(model.Days).Count;
        return Math.Ceiling(count / (double)RowColumns(count)) * MinRowPitch;
    }

    private static double GridHeaderHeight() => Font(DisplaySemiBold, 9).GetHeight() + 2 + Font(Body, 6.5).GetHeight() + 1;

    /// <summary>One 7-column grid per month, Monday first, side by side; returns the y below them.</summary>
    private static double DrawMonthGrids(Canvas c, IReadOnlyList<DayCell> days, DateOnly from, double top, double room)
    {
        var months = Months(days);
        var rows = months.Select(m => WeekRows(m.Year, m.Month)).DefaultIfEmpty(5).Max();
        var pitch = Math.Min(MaxGridPitch, (room - GridHeaderHeight()) / rows);
        var gap = Math.Max(1, pitch * 0.1);
        var cell = pitch - gap;
        var gridWidth = 7 * pitch - gap;
        var byDay = days.ToDictionary(d => d.Day, d => d.Seconds);
        var titleFont = Font(DisplaySemiBold, 9);
        var letterFont = Font(Body, 6.5);
        var numberFont = Font(Body, 6);
        var muted = new XSolidBrush(InkMuted);
        var hairline = new XPen(Line, 0.5);
        var to = days.Count == 0 ? from : days[^1].Day;

        for (var i = 0; i < months.Count; i++)
        {
            var (year, month) = months[i];
            var x0 = Margin + i * (gridWidth + GridGap);
            var y = c.Line(new DateOnly(year, month, 1).ToString("MMMM", Culture), titleFont, new XSolidBrush(Ink), top, x0) + 2;
            var letters = new[] { "M", "T", "W", "T", "F", "S", "S" };
            for (var d = 0; d < 7; d++)
                c.Text(letters[d], letterFont, muted, new XRect(x0 + d * pitch, y, cell, letterFont.GetHeight()), XStringFormats.TopCenter);
            y += letterFont.GetHeight() + 1;

            var lead = ((int)new DateOnly(year, month, 1).DayOfWeek + 6) % 7;
            for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
            {
                var date = new DateOnly(year, month, day);
                var slot = lead + day - 1;
                var x = x0 + slot % 7 * pitch;
                var cy = y + slot / 7 * pitch;
                var r = Math.Min(2, cell / 4);
                if (date < from || date > to || !byDay.TryGetValue(date, out var seconds))
                {
                    c.G.DrawRoundedRectangle(hairline, XBrushes.White, x + 0.25, cy + 0.25, cell - 0.5, cell - 0.5, 2 * r, 2 * r);
                    continue;
                }
                c.G.DrawRoundedRectangle(new XSolidBrush(Shade(seconds)), x, cy, cell, cell, 2 * r, 2 * r);
                c.Text(day.ToString(Culture), numberFont, new XSolidBrush(DayNumberColor(seconds)),
                    new XRect(x, cy, cell, cell), XStringFormats.Center);
            }
        }
        return top + GridHeaderHeight() + rows * pitch - gap;
    }

    private const double MinRowPitch = 9;
    private const double MaxRowPitch = 14;
    private const int MonthsPerColumn = 12;

    /// <summary>Over a year of month rows sits in two columns, so each row stays tall enough to read.</summary>
    private static int RowColumns(int months) => months > MonthsPerColumn ? 2 : 1;

    /// <summary>One row of 31 squares per month, oldest first; past a year, in two columns. Returns the y below them.</summary>
    private static double DrawMonthRows(Canvas c, IReadOnlyList<DayCell> days, DateOnly from, double top, double room)
    {
        var months = Months(days);
        var columns = RowColumns(months.Count);
        var perColumn = (int)Math.Ceiling(months.Count / (double)columns);
        var labelFont = Font(Body, 7.5);
        var labelWidth = c.Measure("May 2026", labelFont).Width + 6;
        var columnWidth = (ContentWidth - (columns - 1) * GridGap) / columns;
        var squarePitch = (columnWidth - labelWidth) / 31;
        var rowPitch = Math.Clamp(room / perColumn, MinRowPitch, Math.Max(MinRowPitch, Math.Min(MaxRowPitch, squarePitch + 2)));
        var square = Math.Min(squarePitch, rowPitch) - Math.Max(1, squarePitch * 0.12);
        var byDay = days.ToDictionary(d => d.Day, d => d.Seconds);
        var muted = new XSolidBrush(InkMuted);
        var hairline = new XPen(Line, 0.5);
        var to = days.Count == 0 ? from : days[^1].Day;
        var r = Math.Min(1.5, square / 4);

        for (var i = 0; i < months.Count; i++)
        {
            var (year, month) = months[i];
            var x0 = Margin + i / perColumn * (columnWidth + GridGap);
            var y = top + i % perColumn * rowPitch;
            c.Text(new DateOnly(year, month, 1).ToString("MMM yyyy", Culture), labelFont, muted,
                new XRect(x0, y, labelWidth, rowPitch), XStringFormats.CenterLeft);
            var sy = y + (rowPitch - square) / 2;
            for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
            {
                var date = new DateOnly(year, month, day);
                var x = x0 + labelWidth + (day - 1) * squarePitch;
                if (date < from || date > to || !byDay.TryGetValue(date, out var seconds))
                    c.G.DrawRoundedRectangle(hairline, XBrushes.White, x + 0.25, sy + 0.25, square - 0.5, square - 0.5, 2 * r, 2 * r);
                else
                    c.G.DrawRoundedRectangle(new XSolidBrush(Shade(seconds)), x, sy, square, square, 2 * r, 2 * r);
            }
        }
        return top + perColumn * rowPitch;
    }

    /// <summary>"No session", the five steps, "3 h or more", right-aligned; the 24-month caption on the left.</summary>
    private static double DrawCalendarLegend(Canvas c, double top, bool cut)
    {
        var font = Font(Body, 8);
        var muted = new XSolidBrush(InkMuted);
        var height = font.GetHeight();
        const double swatch = 8, gap = 2;
        const string low = "No session", high = "3 h or more";

        if (cut)
            c.Text($"Showing the last {HistoryReport.CalendarMaxMonths} months.", font, muted, new XRect(Margin, top, ContentWidth, height), XStringFormats.TopLeft);

        var highWidth = c.Measure(high, font).Width;
        var lowWidth = c.Measure(low, font).Width;
        var x = Right - highWidth - 4 - Ramp.Length * (swatch + gap) + gap - 4 - lowWidth;
        c.Lines.Add($"{low} {high}");
        c.G.DrawString(low, font, muted, new XRect(x, top, lowWidth, height), XStringFormats.TopLeft);
        x += lowWidth + 4;
        foreach (var color in Ramp)
        {
            c.G.DrawRoundedRectangle(new XSolidBrush(color), x, top + (height - swatch) / 2, swatch, swatch, 3, 3);
            x += swatch + gap;
        }
        c.G.DrawString(high, font, muted, new XRect(x - gap + 4, top, highWidth, height), XStringFormats.TopLeft);
        return top + height;
    }

    // ---- Trend line ----

    /// <summary>The trend line with hour gridlines and the dashed study schedule; returns the y below its date labels.</summary>
    private static double DrawTrend(Canvas c, ReportModel model, double plotTop, double plotHeight)
    {
        const double axisWidth = 30, inset = 8;
        var plotLeft = Margin + axisWidth;
        var plotBottom = plotTop + plotHeight;
        var largest = model.Trend.Count == 0 ? 0 : model.Trend.Max(p => p.Seconds);
        // Half an hour of headroom over the schedule line or the largest point, so its label and dots fit.
        double max = Math.Max(model.TrendReferenceSeconds + 1800, Math.Ceiling((largest + 1800) / 1800.0) * 1800);
        double YFor(double seconds) => plotBottom - seconds / max * plotHeight;

        var small = Font(Body, 8);
        var muted = new XSolidBrush(InkMuted);
        var grid = new XPen(Line, 0.5);
        var stepHours = max > 8 * 3600 ? 2 : 1;
        for (var h = 0; h * 3600 <= max; h += stepHours)
        {
            var gy = YFor(h * 3600);
            c.G.DrawLine(grid, plotLeft, gy, Right, gy);
            c.Text(h == 0 ? "0" : $"{h} h", small, muted, new XRect(Margin, gy - 6, axisWidth - 6, 12), XStringFormats.CenterRight);
        }

        var n = model.Trend.Count;
        var points = model.Trend
            .Select((p, i) => new XPoint(
                n == 1 ? (plotLeft + Right) / 2 : plotLeft + inset + i * (Right - plotLeft - 2 * inset) / (n - 1),
                YFor(p.Seconds)))
            .ToArray();

        // The study schedule, dashed and labeled above the line, clear of the data.
        var ry = YFor(model.TrendReferenceSeconds);
        c.G.DrawLine(new XPen(InkMuted, 0.75) { DashPattern = [4, 3] }, plotLeft, ry, Right, ry);
        // An empty period leaves the chart empty rather than drawing a line along zero.
        var drawn = model.Summary.Sessions == 0 ? [] : points;
        DrawReferenceLabel(c, $"Study schedule: {DurationText.Short(model.TrendReferenceSeconds)} a day", small, muted, ry, plotLeft, drawn);

        if (drawn.Length > 1)
            c.G.DrawLines(new XPen(Brand, 2) { LineJoin = XLineJoin.Round, LineCap = XLineCap.Round }, drawn);
        var ring = new XPen(XColors.White, 1);
        var brand = new XSolidBrush(Brand);
        foreach (var p in drawn)
            c.G.DrawEllipse(ring, brand, p.X - 2.6, p.Y - 2.6, 5.2, 5.2);

        // First and last dates only.
        var labelTop = plotBottom + 4;
        if (n > 0)
        {
            var first = model.Trend[0].Label;
            var firstWidth = c.Measure(first, small).Width;
            var fx = n == 1 ? points[0].X - firstWidth / 2 : Math.Max(plotLeft, points[0].X - firstWidth / 2);
            c.Text(first, small, muted, new XRect(fx, labelTop, firstWidth, small.GetHeight()), XStringFormats.TopLeft);
            if (n > 1)
            {
                var last = model.Trend[^1].Label;
                var lastWidth = c.Measure(last, small).Width;
                var lx = Math.Min(Right - lastWidth, points[^1].X - lastWidth / 2);
                c.Text(last, small, muted, new XRect(lx, labelTop, lastWidth, small.GetHeight()), XStringFormats.TopLeft);
            }
        }
        return labelTop + small.GetHeight();
    }

    /// <summary>
    /// Labels the reference line just above it on the right; if a point would sit in the label there,
    /// on the left; if both are taken, on the right over a white backing.
    /// </summary>
    private static void DrawReferenceLabel(Canvas c, string text, XFont font, XBrush brush, double lineY, double plotLeft, IReadOnlyList<XPoint> points)
    {
        var size = c.Measure(text, font);
        var top = lineY - 2 - size.Height;
        bool Clear(double x) => !points.Any(p => p.X + 4 > x && p.X - 4 < x + size.Width && p.Y + 4 > top && p.Y - 4 < lineY);

        var x = Right - size.Width;
        if (!Clear(x) && Clear(plotLeft))
            x = plotLeft;
        else if (!Clear(x))
            c.G.DrawRectangle(XBrushes.White, x - 3, top, size.Width + 3, size.Height);
        c.Text(text, font, brush, new XRect(x, top, size.Width, size.Height), XStringFormats.TopLeft);
    }

    // ---- Page 2: how are they using it? ----

    /// <summary>Draws page 2; returns where Recent Notes ends and where the legal text starts.</summary>
    private static (double NotesBottom, double LegalTop) DrawPage2(Canvas c, ReportModel model)
    {
        var legalTop = DrawLegal(c);
        // Sections sit 18 pt apart. When many profiles and long notes leave less room above the
        // legal text, passes on a scratch page find the overflow: the four gaps share it (6 pt at
        // least), and if that isn't enough the oldest quote shown is dropped, then the next.
        using var scratch = new PdfDocument();
        using var measure = XGraphics.FromPdfPage(AddPage(scratch));
        double NotesEnd(double gap, int quotes) => DrawPage2Sections(new Canvas(measure), model, gap, legalTop, quotes);
        var shown = model.RecentNotes.Count;
        double gap;
        while (true)
        {
            var overflow = NotesEnd(SectionGap, shown) + SectionGap - legalTop;
            gap = overflow <= 0 ? SectionGap : Math.Max(6, SectionGap - overflow / 4);
            // Profile rows stretch to end the notes exactly one gap above the legal text; allow rounding.
            if (shown == 0 || NotesEnd(gap, shown) + gap <= legalTop + 0.01)
                break;
            shown--;
        }
        return (DrawPage2Sections(c, model, gap, legalTop, shown), legalTop);
    }

    /// <summary>Everything on page 2 above the legal text; returns where Recent Notes ends.</summary>
    /// <param name="quotes">How many of the newest notes to quote.</param>
    private static double DrawPage2Sections(Canvas c, ReportModel model, double gap, double legalTop, int quotes)
    {
        var y = BodyTop;

        const double columnGap = 24;
        var columnWidth = (ContentWidth - columnGap) / 2;
        var hourEnd = DrawTimeOfDay(c, model, Margin, columnWidth, y);
        var weekEnd = DrawDayOfWeek(c, model, Margin + columnWidth + columnGap, columnWidth, y);
        y = Math.Max(hourEnd, weekEnd) + gap;

        y = DrawOutcomes(c, model, y) + gap;

        // Profiles get whatever the notes leave above the legal text, 16 to 22 pt a row.
        var notesHeight = NotesHeight(c, model, quotes);
        y = SectionTitle(c, "Profiles", y);
        y = DrawProfiles(c, model.Profiles, y, legalTop - gap - notesHeight - gap - y);
        y += gap;

        return DrawNotes(c, model, y, quotes);
    }

    private static double DrawTimeOfDay(Canvas c, ReportModel model, double left, double width, double top)
    {
        var muted = new XSolidBrush(InkMuted);
        var small = Font(Body, 8);
        var y = SectionTitle(c, "Time of Day", top, left);
        const double plotHeight = 56;
        var plotBottom = y + 6 + plotHeight;
        var slot = width / 24;
        var barWidth = Math.Min(7, slot * 0.7);
        double largest = Math.Max(1, model.StartsByHour.DefaultIfEmpty(0).Max());
        var brand = new XSolidBrush(Brand);
        for (var h = 0; h < 24; h++)
        {
            var count = model.StartsByHour[h];
            if (count == 0)
                continue;
            var height = count / largest * plotHeight;
            c.G.DrawPath(brand, TopRoundedColumn(left + h * slot + (slot - barWidth) / 2, plotBottom - height, barWidth, plotBottom, Math.Min(1.5, barWidth / 2)));
        }
        c.G.DrawLine(new XPen(Line, 0.75), left, plotBottom, left + width, plotBottom);

        var labels = new[] { "12 am", "6 am", "12 pm", "6 pm", "12 am" };
        for (var i = 0; i < labels.Length; i++)
        {
            var x = left + i * 6 * slot;
            var format = i == 0 ? XStringFormats.TopLeft : i == labels.Length - 1 ? XStringFormats.TopRight : XStringFormats.TopCenter;
            var box = i == 0 ? new XRect(x, plotBottom + 3, 40, 10)
                : i == labels.Length - 1 ? new XRect(x - 40, plotBottom + 3, 40, 10)
                : new XRect(x - 20, plotBottom + 3, 40, 10);
            c.Text(labels[i], small, muted, box, format);
        }
        y = plotBottom + 3 + small.GetHeight() + 3;

        var total = model.StartsByHour.Sum();
        var caption = "Session starts by hour.";
        if (total > 0)
        {
            var mornings = (int)Math.Round(100.0 * model.StartsByHour.Take(12).Sum() / total, MidpointRounding.AwayFromZero);
            caption += $" Mornings {mornings}%, afternoons and evenings {100 - mornings}%.";
        }
        foreach (var line in Wrap(c, caption, Font(Body, 9), width))
            y = c.Line(line, Font(Body, 9), muted, y, left);
        return y;
    }

    private static double DrawDayOfWeek(Canvas c, ReportModel model, double left, double width, double top)
    {
        var muted = new XSolidBrush(InkMuted);
        var small = Font(Body, 8);
        var y = SectionTitle(c, "Day of Week", top, left);
        const double plotHeight = 56, axisWidth = 24;
        var plotTop = y + 6;
        var plotBottom = plotTop + plotHeight;
        var plotLeft = left + axisWidth;
        var plotWidth = width - axisWidth;
        // 4 h, the study's daily schedule, is a full bar, as in the app's Last 7 Days chart.
        var largest = model.SecondsByWeekday.DefaultIfEmpty(0).Max();
        double max = Math.Max(HistorySummary.StudyDailySeconds, Math.Ceiling(largest / 3600.0) * 3600);
        var stepHours = max > 8 * 3600 ? 4 : 2;
        var grid = new XPen(Line, 0.5);
        for (var h = 0; h * 3600 <= max; h += stepHours)
        {
            var gy = plotBottom - h * 3600 / max * plotHeight;
            c.G.DrawLine(grid, plotLeft, gy, left + width, gy);
            c.Text(h == 0 ? "0" : $"{h} h", small, muted, new XRect(left, gy - 6, axisWidth - 5, 12), XStringFormats.CenterRight);
        }

        var slot = plotWidth / 7;
        var barWidth = Math.Min(16, slot * 0.55);
        var brand = new XSolidBrush(Brand);
        var names = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        for (var d = 0; d < 7; d++)
        {
            var x = plotLeft + d * slot;
            var seconds = model.SecondsByWeekday[d];
            if (seconds > 0)
            {
                var height = seconds / max * plotHeight;
                c.G.DrawPath(brand, TopRoundedColumn(x + (slot - barWidth) / 2, plotBottom - height, barWidth, plotBottom, Math.Min(2, barWidth / 2)));
            }
            c.Text(names[d], small, muted, new XRect(x, plotBottom + 3, slot, 10), XStringFormats.TopCenter);
        }
        y = plotBottom + 3 + small.GetHeight() + 3;
        foreach (var line in Wrap(c, "Average time per day, including days without a session.", Font(Body, 9), width))
            y = c.Line(line, Font(Body, 9), muted, y, left);
        return y;
    }

    private static readonly XColor[] OutcomeColors = [Brand, Ramp[2], Ramp[1]];

    private static double DrawOutcomes(Canvas c, ReportModel model, double top)
    {
        var o = model.Outcomes;
        var y = SectionTitle(c, "How Sessions Ended", top);
        const double barHeight = 10, gap = 1.5;
        var counts = new[] { o.Completed, o.StoppedEarly, o.EndedEarly };
        var total = counts.Sum();
        var font = Font(Body, 9.5);
        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(InkMuted);

        // Rounded at the bar's two ends only: the segments are clipped to one rounded track.
        var track = new XGraphicsPath();
        track.AddRoundedRectangle(Margin, y, ContentWidth, barHeight, barHeight, barHeight);
        if (total == 0)
        {
            c.G.DrawPath(new XSolidBrush(SurfaceSunken), track);
            return c.Line("No sessions in this period.", font, muted, y + barHeight + 6);
        }
        var state = c.G.Save();
        c.G.IntersectClip(track);
        var shown = counts.Count(n => n > 0);
        var width = ContentWidth - gap * (shown - 1);
        var x = Margin;
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] == 0)
                continue;
            var w = width * counts[i] / total;
            c.G.DrawRectangle(new XSolidBrush(OutcomeColors[i]), x, y, w, barHeight);
            x += w + gap;
        }
        c.G.Restore(state);
        y += barHeight + 6;

        // Words carry each segment's identity; the swatches only connect them to the bar.
        var parts = new[] { $"{o.Completed} completed", $"{o.StoppedEarly} stopped early", $"{o.EndedEarly} ended early." };
        c.Lines.Add(string.Join(" · ", parts));
        const double swatch = 8;
        var height = font.GetHeight();
        x = Margin;
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                c.G.DrawString(" · ", font, muted, new XRect(x, y, 20, height), XStringFormats.TopLeft);
                x += c.Measure(" · ", font).Width;
            }
            c.G.DrawRoundedRectangle(new XSolidBrush(OutcomeColors[i]), x, y + (height - swatch) / 2, swatch, swatch, 3, 3);
            x += swatch + 4;
            c.G.DrawString(parts[i], font, ink, new XRect(x, y, ContentWidth, height), XStringFormats.TopLeft);
            x += c.Measure(parts[i], font).Width;
        }
        y += height + 2;

        var average = o.AveragePlannedSeconds > 0
            ? $"Average session {DurationText.Short(o.AverageSeconds)} of {DurationText.Short(o.AveragePlannedSeconds)} planned."
            : $"Average session {DurationText.Short(o.AverageSeconds)}.";
        return c.Line(average, font, muted, y);
    }

    /// <summary>Time by profile as bars on tracks; returns the y below the last row.</summary>
    private static double DrawProfiles(Canvas c, IReadOnlyList<ProfileRow> profiles, double top, double room)
    {
        var nameFont = Font(Body, 10);
        var valueFont = Font(Body, 9);
        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(InkMuted);

        if (profiles.Count == 0)
            return c.Line("No sessions in this period.", nameFont, muted, top);

        // At most 6 rows; past that, the smallest profiles share an "Other" row.
        const int maxRows = 6;
        var rows = profiles.Count <= maxRows
            ? profiles.ToList()
            : profiles.Take(maxRows - 1)
                .Append(new ProfileRow("Other", profiles.Skip(maxRows - 1).Sum(p => p.Sessions), profiles.Skip(maxRows - 1).Sum(p => p.Seconds)))
                .ToList();

        const double gap = 12, track = 8;
        var rowHeight = Math.Clamp(room / rows.Count, 16, 22);
        var values = rows.Select(r => $"{DurationText.Short(r.Seconds)} · {Count(r.Sessions, "session")}").ToList();
        var valueWidth = values.Max(v => c.Measure(v, valueFont).Width);
        var nameWidth = Math.Min(150, rows.Max(r => c.Measure(UserText(r.Name), nameFont).Width));
        var trackX = Margin + nameWidth + gap;
        var trackWidth = Right - valueWidth - gap - trackX;
        double largest = rows.Max(r => r.Seconds);

        for (var i = 0; i < rows.Count; i++)
        {
            var y = top + i * rowHeight;
            var name = Fit(c, UserText(rows[i].Name), nameFont, nameWidth);
            c.Text(name, nameFont, ink, new XRect(Margin, y, nameWidth, rowHeight), XStringFormats.CenterLeft);
            c.Boxes.Add(new Box("name", y, new XRect(Margin, y, c.Measure(name, nameFont).Width, rowHeight)));

            var ty = y + (rowHeight - track) / 2;
            c.G.DrawRoundedRectangle(new XSolidBrush(SurfaceSunken), trackX, ty, trackWidth, track, track, track);
            c.Boxes.Add(new Box("track", y, new XRect(trackX, ty, trackWidth, track)));
            var fill = largest == 0 ? 0 : trackWidth * rows[i].Seconds / largest;
            if (fill > 0)
            {
                var r = Math.Min(track, fill);
                c.G.DrawRoundedRectangle(new XSolidBrush(Brand), trackX, ty, fill, track, r, r);
            }

            c.Text(values[i], valueFont, muted, new XRect(Margin, y, ContentWidth, rowHeight), XStringFormats.CenterRight);
        }
        return top + rows.Count * rowHeight;
    }

    private const int MaxQuoteLines = 3;

    /// <summary>
    /// The newest <paramref name="quotes"/> quotes' lines, at most 3 each (the third cut with an
    /// ellipsis), and whether the date needs a line of its own after the last.
    /// </summary>
    private static List<(string Date, bool DateOnOwnLine, List<string> Lines)> QuoteLines(Canvas c, ReportModel model, int quotes)
    {
        var font = Font(Body, 9.5);
        return model.RecentNotes.Take(quotes).Select(n =>
        {
            var words = string.Join(" ", n.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var text = "\u201c" + BreakLongWords(UserText(words)) + "\u201d";
            var date = n.Day.ToString(ShortDateFormat, Culture);
            var lines = Wrap(c, text, font, ContentWidth);
            if (lines.Count > MaxQuoteLines)
            {
                var last = lines[MaxQuoteLines - 1];
                while (last.Length > 0 && c.Measure(last + "\u2026\u201d", font).Width > ContentWidth)
                    last = last[..^1];
                lines = [.. lines.Take(MaxQuoteLines - 1), last.TrimEnd() + "\u2026\u201d"];
            }
            var fits = c.Measure(lines[^1] + "  " + date, font).Width <= ContentWidth;
            return (date, !fits, lines);
        }).ToList();
    }

    private static double NotesHeight(Canvas c, ReportModel model, int quotes)
    {
        var line = Font(Body, 9.5).GetHeight();
        var height = Font(DisplaySemiBold, 13).GetHeight() + 6;
        foreach (var q in QuoteLines(c, model, quotes))
            height += (q.Lines.Count + (q.DateOnOwnLine ? 1 : 0)) * line + 4;
        return height + Font(Body, 9).GetHeight();
    }

    private static double DrawNotes(Canvas c, ReportModel model, double top, int quotes)
    {
        var font = Font(Body, 9.5);
        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(InkMuted);
        var y = SectionTitle(c, "Recent Notes", top);
        var height = font.GetHeight();

        foreach (var (date, ownLine, lines) in QuoteLines(c, model, quotes))
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var last = i == lines.Count - 1;
                c.Boxes.Add(new Box("quote", y, new XRect(Margin, y, c.Measure(lines[i], font).Width, height)));
                if (!last || ownLine)
                {
                    y = c.Line(lines[i], font, ink, y);
                    continue;
                }
                c.Lines.Add(lines[i] + "  " + date);
                c.G.DrawString(lines[i], font, ink, new XRect(Margin, y, ContentWidth, height), XStringFormats.TopLeft);
                var x = Margin + c.Measure(lines[i] + "  ", font).Width;
                c.G.DrawString(date, font, muted, new XRect(x, y, Right - x, height), XStringFormats.TopLeft);
                c.Boxes.Add(new Box("date", y, new XRect(x, y, c.Measure(date, font).Width, height)));
                y += height;
            }
            if (ownLine)
            {
                c.Boxes.Add(new Box("date", y, new XRect(Margin, y, c.Measure(date, font).Width, height)));
                y = c.Line(date, font, muted, y);
            }
            y += 4;
        }

        var count = model.NoteCount == 0 ? "No notes in this period." : $"{Count(model.NoteCount, "note")} in this period.";
        return c.Line(count, Font(Body, 9), muted, y);
    }

    /// <summary>About These Numbers and both legal notices, set from the bottom of page 2 up; returns their top.</summary>
    private static double DrawLegal(Canvas c)
    {
        var font = Font(Body, 9);
        var bold = Font(Body, 9, bold: true);
        var ink = new XSolidBrush(Ink);
        var lineHeight = font.GetHeight();
        const string important = "Important:";
        var notice = LegalNotices.ImplantedDeviceNotice;

        var about = Wrap(c, AboutNumbers, font, ContentWidth);
        var disclaimer = Wrap(c, LegalNotices.MedicalDisclaimer, font, ContentWidth);
        // Wrapped as one run so the bold word's width is close enough to the regular one's; the
        // first line is re-checked below with the bold word measured as set.
        var noticeLines = Wrap(c, notice, font, ContentWidth - (c.Measure(important, bold).Width - c.Measure(important, font).Width));
        const double paragraphGap = 6;
        var titleHeight = Font(DisplaySemiBold, 13).GetHeight() + 6;
        var height = titleHeight + (about.Count + disclaimer.Count + noticeLines.Count) * lineHeight + 2 * paragraphGap;

        var top = BodyBottom - height;
        var y = SectionTitle(c, "About These Numbers", top);
        foreach (var line in about)
            y = c.Line(line, font, ink, y);
        y += paragraphGap;
        foreach (var line in disclaimer)
            y = c.Line(line, font, ink, y);
        y += paragraphGap;
        for (var i = 0; i < noticeLines.Count; i++)
        {
            if (i > 0)
            {
                y = c.Line(noticeLines[i], font, ink, y);
                continue;
            }
            c.Lines.Add(noticeLines[0]);
            c.G.DrawString(important, bold, ink, new XRect(Margin, y, ContentWidth, lineHeight), XStringFormats.TopLeft);
            var x = Margin + c.Measure(important, bold).Width;
            c.G.DrawString(noticeLines[0][important.Length..], font, ink, new XRect(x, y, Right - x, lineHeight), XStringFormats.TopLeft);
            y += lineHeight;
        }
        return top;
    }

    // ---- Helpers ----

    /// <summary>A column with its top corners rounded and a square base.</summary>
    private static XGraphicsPath TopRoundedColumn(double x, double top, double width, double bottom, double radius)
    {
        radius = Math.Min(radius, bottom - top);
        var path = new XGraphicsPath();
        path.AddLine(x, bottom, x, top + radius);
        if (radius > 0)
            path.AddArc(x, top, 2 * radius, 2 * radius, 180, 90);
        path.AddLine(x + radius, top, x + width - radius, top);
        if (radius > 0)
            path.AddArc(x + width - 2 * radius, top, 2 * radius, 2 * radius, 270, 90);
        path.AddLine(x + width, top + radius, x + width, bottom);
        path.CloseFigure();
        return path;
    }

    private static XFont Font(string family, double size, bool bold = false) =>
        new(family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));

    private static List<string> Wrap(Canvas c, string text, XFont font, double width)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' '))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && c.Measure(candidate, font).Width > width)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }
        lines.Add(current);
        return lines;
    }

    /// <summary>Shortens <paramref name="text"/> with an ellipsis until it fits <paramref name="width"/>.</summary>
    private static string Fit(Canvas c, string text, XFont font, double width)
    {
        if (c.Measure(text, font).Width <= width)
            return text;
        for (var n = text.Length - 1; n > 0; n--)
        {
            var shorter = text[..n].TrimEnd() + "…";
            if (c.Measure(shorter, font).Width <= width)
                return shorter;
        }
        return "…";
    }

    /// <summary>
    /// The report wraps only at spaces, so a longer unbroken run (a pasted link) would run off the
    /// page. Puts a plain space after every <see cref="MaxWordLength"/> characters of such a run;
    /// the fonts have no glyph for a zero-width space.
    /// </summary>
    internal static string BreakLongWords(string text)
    {
        var result = new StringBuilder(text.Length + text.Length / MaxWordLength);
        var run = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                run = 0;
            }
            else
            {
                // Never between the two halves of a surrogate pair.
                if (run >= MaxWordLength && !char.IsLowSurrogate(c))
                {
                    result.Append(' ');
                    run = 0;
                }
                run++;
            }
            result.Append(c);
        }
        return result.ToString();
    }

    /// <summary>Whether the body font has a glyph for <paramref name="rune"/>, per its cmap.</summary>
    internal static bool CanRender(Rune rune) => _bodyGlyphs.Contains(rune.Value);

    /// <summary>
    /// Text a person typed (notes, profile names) as the body font can set it: every character it
    /// has no glyph for becomes "?", so what is measured is what is drawn. ICU's narrow no-break
    /// space, which pasted times carry, becomes a plain space. The CSV keeps the exact text.
    /// </summary>
    private static string UserText(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var rune in text.Replace('\u202F', ' ').EnumerateRunes())
        {
            if (CanRender(rune))
                result.Append(rune.ToString());
            else
                result.Append('?');
        }
        return result.ToString();
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}


/// <summary>
/// Maps report font families to the app's TTFs: "Lexend" to Lexend-Bold when bold, else Lexend-Medium;
/// "Lexend SemiBold" to Lexend-SemiBold; anything else to Atkinson Hyperlegible Next Bold or Regular.
/// </summary>
internal sealed class ReportFontResolver(Func<string, byte[]> load) : IFontResolver
{
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) => familyName switch
    {
        "Lexend" => new FontResolverInfo(bold ? "Lexend-Bold" : "Lexend-Medium"),
        "Lexend SemiBold" => new FontResolverInfo("Lexend-SemiBold"),
        _ => new FontResolverInfo(bold ? "AtkinsonHyperlegibleNext-Bold" : "AtkinsonHyperlegibleNext-Regular"),
    };

    public byte[]? GetFont(string faceName) => load(faceName);
}

/// <summary>Reads which code points a TrueType font maps to a glyph, from its cmap table.</summary>
internal static class FontCmap
{
    public static HashSet<int> CodePoints(byte[] font)
    {
        var points = new HashSet<int>();
        var cmap = Table(font, "cmap");
        if (cmap < 0)
            return points;

        // Prefer a full-Unicode subtable (format 12), else the BMP one (format 4).
        int format4 = -1, format12 = -1;
        for (var i = 0; i < U16(font, cmap + 2); i++)
        {
            var record = cmap + 4 + 8 * i;
            var platform = U16(font, record);
            var encoding = U16(font, record + 2);
            var offset = cmap + (int)U32(font, record + 4);
            var unicode = platform == 0 || platform == 3 && (encoding == 1 || encoding == 10);
            if (!unicode)
                continue;
            if (U16(font, offset) == 12)
                format12 = offset;
            else if (U16(font, offset) == 4)
                format4 = offset;
        }

        if (format12 >= 0)
        {
            for (var g = 0; g < U32(font, format12 + 12); g++)
            {
                var group = format12 + 16 + 12 * g;
                var start = (int)U32(font, group);
                var end = (int)U32(font, group + 4);
                var glyph = U32(font, group + 8);
                for (var cp = start; cp <= end; cp++)
                {
                    if (glyph + (uint)(cp - start) != 0)
                        points.Add(cp);
                }
            }
        }
        else if (format4 >= 0)
        {
            var segments = U16(font, format4 + 6) / 2;
            var ends = format4 + 14;
            var starts = ends + 2 * segments + 2;
            var deltas = starts + 2 * segments;
            var rangeOffsets = deltas + 2 * segments;
            for (var s = 0; s < segments; s++)
            {
                int end = U16(font, ends + 2 * s), start = U16(font, starts + 2 * s);
                var delta = (short)U16(font, deltas + 2 * s);
                var rangeOffset = U16(font, rangeOffsets + 2 * s);
                for (var cp = start; cp <= end && cp != 0xFFFF; cp++)
                {
                    var glyph = rangeOffset == 0
                        ? (cp + delta) & 0xFFFF
                        : U16(font, rangeOffsets + 2 * s + rangeOffset + 2 * (cp - start));
                    if (rangeOffset != 0 && glyph != 0)
                        glyph = (glyph + delta) & 0xFFFF;
                    if (glyph != 0)
                        points.Add(cp);
                }
            }
        }
        return points;
    }

    private static int Table(byte[] font, string tag)
    {
        for (var i = 0; i < U16(font, 4); i++)
        {
            var record = 12 + 16 * i;
            if (Encoding.ASCII.GetString(font, record, 4) == tag)
                return (int)U32(font, record + 8);
        }
        return -1;
    }

    private static ushort U16(byte[] b, int o) => (ushort)(b[o] << 8 | b[o + 1]);

    private static uint U32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
}
