using System.Globalization;
using System.Reflection;
using BuzzahBuddy.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using MdColor = MigraDoc.DocumentObjectModel.Color;
using VerticalAlignment = MigraDoc.DocumentObjectModel.Tables.VerticalAlignment;

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
/// Draws the shared session report as a US Letter PDF in the design system's light theme.
/// Page 1 (Overview) is drawn with XGraphics for rounded chart columns; the tables after it are
/// MigraDoc flow, rendered into the same document. Holds no MAUI types.
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
    private static readonly XColor SurfaceSunken = XColor.FromArgb(0xeb, 0xef, 0xf6);
    private static readonly XColor Line = XColor.FromArgb(0xd6, 0xde, 0xea);

    // US Letter, 0.75 in margins.
    private const double PageWidth = 612;
    private const double PageHeight = 792;
    private const double Margin = 54;
    private const double ContentWidth = PageWidth - 2 * Margin;
    private const double Right = PageWidth - Margin;

    private const double MarkHeight = 18;
    private const double RuleY = Margin + MarkHeight + 8;
    private const double BodyTop = RuleY + 22;
    private const double FooterBottom = PageHeight - Margin;
    private const double BodyBottom = FooterBottom - 24;

    private const string DateFormat = "MMM d, yyyy";
    private const string ShortDateFormat = "MMM d";

    private const string AboutNumbers =
        "Times are what BuzzahBuddy recorded on this phone. Paused time isn't counted. Sessions your gloves ran while this phone wasn't connected may be missing.";

    private const string ChartCaption =
        "Early studies of vibrotactile coordinated reset used 2 hours, twice a day (Pfeifer et al. 2021). The dashed line marks that schedule.";

    private static readonly object FontGate = new();
    private static bool _fontsReady;

    /// <summary>The PDF for <paramref name="model"/>, with dates and times in <paramref name="culture"/>.</summary>
    public static byte[] Render(ReportModel model, ReportAssets assets, CultureInfo culture)
    {
        EnsureFonts(assets);

        using var pdf = new PdfDocument();
        pdf.Info.Title = Title;
        pdf.Info.Subject = $"{model.From.ToString(DateFormat, culture)} to {model.To.ToString(DateFormat, culture)}";
        pdf.Info.Author = "BuzzahBuddy";
        pdf.Info.Creator = "BuzzahBuddy";
        pdf.Options.CompressContentStreams = true;

        var renderer = new DocumentRenderer(FlowDocument(model, assets.BrandMarkPng, culture));
        renderer.PrepareDocument();
        var total = renderer.FormattedDocument!.PageCount + 1;

        using var markStream = new MemoryStream(assets.BrandMarkPng);
        using var mark = XImage.FromStream(markStream);

        using (var gfx = XGraphics.FromPdfPage(AddPage(pdf)))
        {
            DrawHeader(gfx, mark);
            DrawOverview(gfx, model, culture);
            DrawFooter(gfx, 1, total);
        }

        for (var n = 1; n < total; n++)
        {
            using var gfx = XGraphics.FromPdfPage(AddPage(pdf));
            renderer.RenderPage(gfx, n);
            DrawFooter(gfx, n + 1, total);
        }

        using var output = new MemoryStream();
        pdf.Save(output, false);
        return output.ToArray();
    }

    /// <summary>The font resolver can be set once per process, so the first assets given serve every report.</summary>
    private static void EnsureFonts(ReportAssets assets)
    {
        lock (FontGate)
        {
            if (_fontsReady)
                return;
            GlobalFontSettings.FontResolver = new ReportFontResolver(assets.LoadFont);
            // MigraDoc's text then carries Unicode maps too, so any note text extracts and copies.
            GlobalFontSettings.DefaultFontEncoding = PdfFontEncoding.Unicode;
            _fontsReady = true;
        }
    }

    private static PdfPage AddPage(PdfDocument pdf)
    {
        var page = pdf.AddPage();
        page.Size = PageSize.Letter;
        return page;
    }

    // ---- Page furniture (XGraphics) ----

    private static double MarkWidth(XImage mark) => MarkHeight * mark.PixelWidth / mark.PixelHeight;

    private static void DrawHeader(XGraphics gfx, XImage mark)
    {
        var markWidth = MarkWidth(mark);
        gfx.DrawImage(mark, Margin, Margin, markWidth, MarkHeight);
        var x = Margin + markWidth + 6;
        gfx.DrawString("BlueBuzzah", Font(DisplaySemiBold, 10), new XSolidBrush(Ink),
            new XRect(x, Margin, Right - x, MarkHeight), XStringFormats.CenterLeft);
        gfx.DrawString("Session Report", Font(Body, 9), new XSolidBrush(InkMuted),
            new XRect(Margin, Margin, ContentWidth, MarkHeight), XStringFormats.CenterRight);
        gfx.DrawLine(new XPen(Line, 0.75), Margin, RuleY, Right, RuleY);
    }

    private static void DrawFooter(XGraphics gfx, int page, int total)
    {
        var font = Font(Body, 8);
        var brush = new XSolidBrush(InkMuted);
        var box = new XRect(Margin, FooterBottom - font.GetHeight(), ContentWidth, font.GetHeight());
        gfx.DrawString("BuzzahBuddy", font, brush, box, XStringFormats.BottomLeft);
        gfx.DrawString($"Page {page} of {total}", font, brush, box, XStringFormats.BottomRight);
    }

    // ---- Page 1: Overview (XGraphics) ----

    private static void DrawOverview(XGraphics gfx, ReportModel model, CultureInfo culture)
    {
        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(InkMuted);
        var sum = model.Summary;
        var y = BodyTop;

        y = TextLine(gfx, "Session Report", Font(Display, 24, bold: true), ink, y);
        y += 4;
        y = TextLine(gfx,
            $"{model.From.ToString(DateFormat, culture)} to {model.To.ToString(DateFormat, culture)} · {Count(sum.DaysInPeriod, "day")}",
            Font(Body, 11), muted, y);
        y += 2;
        y = TextLine(gfx, $"Created {model.CreatedAt.ToString(DateFormat, culture)}", Font(Body, 9), muted, y);
        y += 18;

        y = DrawTiles(gfx, y, new[]
        {
            new Tile("Days With Sessions", $"{sum.DaysWithSessions} of {sum.DaysInPeriod}", null),
            new Tile("Total Time", DurationText.Short(sum.TherapySeconds), null),
            new Tile("Average per Session Day", DurationText.Short(sum.AverageSecondsPerSessionDay), null),
            new Tile("Sessions", sum.Sessions.ToString(culture),
                $"{sum.Completed} completed · {sum.StoppedEarly} stopped early · {sum.EndedEarly} ended early"),
        });
        y += 22;

        y = TextLine(gfx, model.ChartTitle, Font(DisplaySemiBold, 13), ink, y);
        y = DrawChart(gfx, model, y + 18);
        y += 8;
        foreach (var line in Wrap(gfx, ChartCaption, Font(Body, 9), ContentWidth))
            y = TextLine(gfx, line, Font(Body, 9), muted, y);
        y += 22;

        y = TextLine(gfx, "Time by Profile", Font(DisplaySemiBold, 13), ink, y);
        DrawProfiles(gfx, model.Profiles, y + 8);
    }

    private sealed record Tile(string Label, string Value, string? Detail);

    private static double DrawTiles(XGraphics gfx, double top, IReadOnlyList<Tile> tiles)
    {
        const double gap = 10, padding = 12;
        var width = (ContentWidth - gap * (tiles.Count - 1)) / tiles.Count;
        var inner = width - 2 * padding;
        var labelFont = Font(Body, 9);
        var detailFont = Font(Body, 8.5);

        // One value size for every tile: 18 pt, or smaller if a value wouldn't fit its tile.
        var valueSize = 18.0;
        foreach (var t in tiles)
        {
            var w = gfx.MeasureString(t.Value, Font(DisplaySemiBold, 18)).Width;
            if (w > inner)
                valueSize = Math.Min(valueSize, Math.Floor(18 * inner / w * 2) / 2);
        }
        var valueFont = Font(DisplaySemiBold, valueSize);

        var labels = tiles.Select(t => Wrap(gfx, t.Label, labelFont, inner)).ToList();
        var details = tiles.Select(t => t.Detail is null ? new List<string>() : Wrap(gfx, t.Detail, detailFont, inner)).ToList();
        var labelHeight = labels.Max(l => l.Count) * labelFont.GetHeight();
        var detailHeight = details.Max(d => d.Count) * detailFont.GetHeight();
        var height = padding + labelHeight + 4 + valueFont.GetHeight() + (detailHeight > 0 ? 2 + detailHeight : 0) + padding;

        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(InkMuted);
        for (var i = 0; i < tiles.Count; i++)
        {
            var x = Margin + i * (width + gap);
            gfx.DrawRoundedRectangle(new XSolidBrush(SurfaceSunken), x, top, width, height, 16, 16);
            var y = top + padding;
            foreach (var line in labels[i])
                y = TextLine(gfx, line, labelFont, muted, y, x + padding);
            // Values share one baseline even when a label wraps.
            y = top + padding + labelHeight + 4;
            y = TextLine(gfx, tiles[i].Value, valueFont, ink, y, x + padding);
            y += 2;
            foreach (var line in details[i])
                y = TextLine(gfx, line, detailFont, muted, y, x + padding);
        }
        return top + height;
    }

    /// <summary>The column chart; returns the y below its date labels.</summary>
    private static double DrawChart(XGraphics gfx, ReportModel model, double plotTop)
    {
        const double plotHeight = 150, axisWidth = 30;
        var plotLeft = Margin + axisWidth;
        var plotWidth = Right - plotLeft;
        var plotBottom = plotTop + plotHeight;
        double max = model.ChartMaxSeconds;
        double YFor(double seconds) => plotBottom - seconds / max * plotHeight;

        var small = Font(Body, 8);
        var muted = new XSolidBrush(InkMuted);

        // Hour gridlines, every 7th hour past 12 h so the labels stay apart.
        var stepHours = max > 12 * 3600 ? 7 : 1;
        var grid = new XPen(Line, 0.5);
        for (var h = 0; h * 3600 <= max; h += stepHours)
        {
            var gy = YFor(h * 3600);
            gfx.DrawLine(grid, plotLeft, gy, Right, gy);
            gfx.DrawString(h == 0 ? "0" : $"{h} h", small, muted,
                new XRect(Margin, gy - 6, axisWidth - 6, 12), XStringFormats.CenterRight);
        }

        var n = model.Chart.Count;
        var slot = plotWidth / n;
        var gap = Math.Min(2, slot * 0.3);
        var width = (plotWidth - gap * (n - 1)) / n;
        var brand = new XSolidBrush(Brand);
        var nextLabelX = double.MaxValue;
        for (var i = n - 1; i >= 0; i--)
        {
            var p = model.Chart[i];
            var x = plotLeft + i * (width + gap);
            if (p.Seconds > 0)
                gfx.DrawPath(brand, TopRoundedColumn(x, YFor(p.Seconds), width, plotBottom, Math.Min(2, width / 2)));
            if (p.Label.Length == 0)
                continue;
            // Centered under its column but kept inside the plot; a label that would run into
            // the newer one beside it is left out.
            var labelWidth = gfx.MeasureString(p.Label, small).Width;
            var lx = Math.Clamp(x + width / 2 - labelWidth / 2, plotLeft, Right - labelWidth);
            if (lx + labelWidth + 6 > nextLabelX)
                continue;
            gfx.DrawString(p.Label, small, muted, new XRect(lx, plotBottom + 4, labelWidth, 10), XStringFormats.TopLeft);
            nextLabelX = lx;
        }

        // The study schedule, dashed and labeled directly above the line on the right.
        var ry = YFor(model.ChartReferenceSeconds);
        var dashed = new XPen(InkMuted, 0.75) { DashPattern = [4, 3] };
        gfx.DrawLine(dashed, plotLeft, ry, Right, ry);
        var daily = model.ChartReferenceSeconds == HistorySummary.StudyDailySeconds;
        var refLabel = $"Study schedule: {DurationText.Short(model.ChartReferenceSeconds)} {(daily ? "a day" : "a week")}";
        DrawReferenceLabel(gfx, refLabel, small, muted, ry, plotLeft, width, gap, model.Chart.Select(p => YFor(p.Seconds)).ToList());

        return plotBottom + 4 + small.GetHeight();
    }

    /// <summary>
    /// Labels the reference line just above it on the right; if a taller column would cover the
    /// label there, on the left; if both are covered, on the right over a white backing.
    /// </summary>
    private static void DrawReferenceLabel(XGraphics gfx, string text, XFont font, XBrush brush, double lineY,
        double plotLeft, double columnWidth, double gap, IReadOnlyList<double> columnTops)
    {
        var size = gfx.MeasureString(text, font);
        var top = lineY - 2 - size.Height;
        bool Clear(double x) => !columnTops
            .Select((columnTop, i) => (columnTop, x0: plotLeft + i * (columnWidth + gap)))
            .Any(c => c.columnTop < lineY - 2 && c.x0 < x + size.Width && c.x0 + columnWidth > x);

        var x = Right - size.Width;
        if (!Clear(x) && Clear(plotLeft))
            x = plotLeft;
        else if (!Clear(x))
            gfx.DrawRectangle(XBrushes.White, x - 3, top, size.Width + 3, size.Height);
        gfx.DrawString(text, font, brush, new XRect(x, top, size.Width, size.Height), XStringFormats.TopLeft);
    }

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

    private static void DrawProfiles(XGraphics gfx, IReadOnlyList<ProfileRow> profiles, double top)
    {
        var nameFont = Font(Body, 10);
        var valueFont = Font(Body, 9);
        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(InkMuted);

        if (profiles.Count == 0)
        {
            TextLine(gfx, "No sessions in this period.", nameFont, muted, top);
            return;
        }

        // At most 6 rows; past that, the smallest profiles share an "Other" row.
        const int maxRows = 6;
        var rows = profiles.Count <= maxRows
            ? profiles.ToList()
            : profiles.Take(maxRows - 1)
                .Append(new ProfileRow("Other", profiles.Skip(maxRows - 1).Sum(p => p.Sessions), profiles.Skip(maxRows - 1).Sum(p => p.Seconds)))
                .ToList();

        const double gap = 12, track = 8;
        // 24 pt rows, tightened (never below 16 pt) when tall tiles leave less room above the footer.
        var rowHeight = Math.Clamp((BodyBottom - top) / rows.Count, 16, 24);
        var values = rows.Select(r => $"{DurationText.Short(r.Seconds)} · {Count(r.Sessions, "session")}").ToList();
        var valueWidth = values.Max(v => gfx.MeasureString(v, valueFont).Width);
        var nameWidth = Math.Min(150, rows.Max(r => gfx.MeasureString(r.Name, nameFont).Width));
        var trackX = Margin + nameWidth + gap;
        var trackWidth = Right - valueWidth - gap - trackX;
        double largest = rows.Max(r => r.Seconds);

        for (var i = 0; i < rows.Count; i++)
        {
            var y = top + i * rowHeight;
            var box = new XRect(Margin, y, nameWidth, rowHeight);
            gfx.DrawString(Fit(gfx, rows[i].Name, nameFont, nameWidth), nameFont, ink, box, XStringFormats.CenterLeft);

            var ty = y + (rowHeight - track) / 2;
            gfx.DrawRoundedRectangle(new XSolidBrush(SurfaceSunken), trackX, ty, trackWidth, track, track, track);
            var fill = largest == 0 ? 0 : trackWidth * rows[i].Seconds / largest;
            if (fill > 0)
            {
                var r = Math.Min(track, fill);
                gfx.DrawRoundedRectangle(new XSolidBrush(Brand), trackX, ty, fill, track, r, r);
            }

            gfx.DrawString(values[i], valueFont, muted, new XRect(Margin, y, ContentWidth, rowHeight), XStringFormats.CenterRight);
        }
    }

    // ---- Pages 2+: tables (MigraDoc) ----

    private static Document FlowDocument(ReportModel model, byte[] markPng, CultureInfo culture)
    {
        var doc = new Document();
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = Body;
        normal.Font.Size = 9.5;
        normal.Font.Color = Md(Ink);

        var heading = doc.Styles.AddStyle("SectionTitle", StyleNames.Normal);
        heading.Font.Name = DisplaySemiBold;
        heading.Font.Size = 13;
        heading.ParagraphFormat.SpaceBefore = Unit.FromPoint(22);
        heading.ParagraphFormat.SpaceAfter = Unit.FromPoint(8);
        heading.ParagraphFormat.KeepWithNext = true;

        var section = doc.AddSection();
        var setup = section.PageSetup;
        setup.PageWidth = Unit.FromPoint(PageWidth);
        setup.PageHeight = Unit.FromPoint(PageHeight);
        setup.LeftMargin = setup.RightMargin = Unit.FromPoint(Margin);
        setup.TopMargin = Unit.FromPoint(BodyTop);
        setup.BottomMargin = Unit.FromPoint(PageHeight - BodyBottom);
        setup.HeaderDistance = Unit.FromPoint(Margin);

        AddHeader(section, markPng);

        // The first heading sits at the top of the page, like page 1's title.
        var first = section.AddParagraph("By Week", "SectionTitle");
        first.Format.SpaceBefore = 0;
        AddWeeks(section, model.Weeks, culture);

        section.AddParagraph("Daily Log", "SectionTitle");
        AddDailyLog(section, model.LogDays, culture);

        // The closing section and both notices stay together on one page.
        section.AddParagraph("About These Numbers", "SectionTitle");
        section.AddParagraph(AboutNumbers).Format.KeepWithNext = true;

        var disclaimer = section.AddParagraph(LegalNotices.MedicalDisclaimer);
        disclaimer.Format.SpaceBefore = Unit.FromPoint(12);
        disclaimer.Format.KeepWithNext = true;
        var notice = section.AddParagraph();
        notice.Format.SpaceBefore = Unit.FromPoint(8);
        const string important = "Important:";
        notice.AddFormattedText(important, TextFormat.Bold);
        notice.AddText(LegalNotices.ImplantedDeviceNotice[important.Length..]);
        return doc;
    }

    /// <summary>The same header as page 1: mark, wordmark, "Session Report", then a rule 8 pt below.</summary>
    private static void AddHeader(Section section, byte[] markPng)
    {
        using var stream = new MemoryStream(markPng);
        using var mark = XImage.FromStream(stream);
        var markWidth = MarkWidth(mark);

        var table = section.Headers.Primary.AddTable();
        table.LeftPadding = table.RightPadding = table.TopPadding = 0;
        table.BottomPadding = Unit.FromPoint(RuleY - Margin - MarkHeight);
        table.AddColumn(Unit.FromPoint(markWidth + 6));
        table.AddColumn(Unit.FromPoint(ContentWidth / 2 - markWidth - 6));
        table.AddColumn(Unit.FromPoint(ContentWidth / 2));

        var row = table.AddRow();
        row.Height = Unit.FromPoint(MarkHeight);
        row.HeightRule = RowHeightRule.AtLeast;
        row.VerticalAlignment = VerticalAlignment.Center;
        row.Borders.Bottom.Width = Unit.FromPoint(0.75);
        row.Borders.Bottom.Color = Md(Line);

        var image = row.Cells[0].AddImage("base64:" + Convert.ToBase64String(markPng));
        image.Height = Unit.FromPoint(MarkHeight);
        image.LockAspectRatio = true;

        var word = row.Cells[1].AddParagraph("BlueBuzzah");
        word.Format.Font.Name = DisplaySemiBold;
        word.Format.Font.Size = 10;

        var label = row.Cells[2].AddParagraph("Session Report");
        label.Format.Alignment = ParagraphAlignment.Right;
        label.Format.Font.Size = 9;
        label.Format.Font.Color = Md(InkMuted);
    }

    private static void AddWeeks(Section section, IReadOnlyList<WeekRow> weeks, CultureInfo culture)
    {
        var table = NewTable(section, 200, 152, 152);
        AddHeaderRow(table, "Week", "Days With Sessions", "Session Time");
        foreach (var w in weeks)
        {
            var row = table.AddRow();
            HairlineBelow(row);
            row.Cells[0].AddParagraph($"{w.Start.ToString(ShortDateFormat, culture)} to {w.End.ToString(ShortDateFormat, culture)}");
            row.Cells[1].AddParagraph($"{w.DaysWithSessions} of {w.End.DayNumber - w.Start.DayNumber + 1}");
            row.Cells[2].AddParagraph(DurationText.Short(w.Seconds));
        }
    }

    private static void AddDailyLog(Section section, IReadOnlyList<DaySummary> days, CultureInfo culture)
    {
        if (days.Count == 0)
        {
            section.AddParagraph("No sessions in this period.");
            return;
        }

        var table = NewTable(section, 100, 64, 80, 140, 120);
        AddHeaderRow(table, "Date", "Start", "Length", "Profile", "Outcome");
        foreach (var d in days)
        {
            var hasNote = !string.IsNullOrEmpty(d.Note);
            var rows = new List<Row>();

            if (d.Sessions.Count == 0)
            {
                var row = table.AddRow();
                row.Cells[1].AddParagraph("No sessions");
                rows.Add(row);
            }
            foreach (var s in d.Sessions)
            {
                var row = table.AddRow();
                row.Cells[1].AddParagraph(Printable(s.StartedAt.ToString(culture.DateTimeFormat.ShortTimePattern, culture)));
                row.Cells[2].AddParagraph(DurationText.Short(s.TherapySeconds));
                row.Cells[3].AddParagraph(s.ProfileName);
                row.Cells[4].AddParagraph(HistoryText.OutcomeText(s.Outcome));
                rows.Add(row);
            }
            rows[0].Cells[0].AddParagraph(d.Day.ToString(DateFormat, culture));

            if (hasNote)
            {
                var row = table.AddRow();
                row.Cells[0].MergeRight = 4;
                var note = row.Cells[0].AddParagraph();
                note.Format.Font.Color = Md(InkMuted);
                var lines = d.Note!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                note.AddText("Note: " + lines[0]);
                foreach (var line in lines.Skip(1))
                {
                    note.AddLineBreak();
                    note.AddText(line);
                }
                rows.Add(row);
            }

            // A day's rows stay on one page, with one hairline under the day.
            rows[0].KeepWith = rows.Count - 1;
            HairlineBelow(rows[^1]);
        }
    }

    private static Table NewTable(Section section, params double[] widths)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(6);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(4);
        // Unset, MigraDoc hangs a table left by its cell padding; 0 puts its edges on the margins.
        table.Rows.LeftIndent = 0;
        foreach (var w in widths)
            table.AddColumn(Unit.FromPoint(w));
        return table;
    }

    /// <summary>A heading row on <c>surface-sunken</c> that repeats on every page.</summary>
    private static void AddHeaderRow(Table table, params string[] labels)
    {
        var row = table.AddRow();
        row.HeadingFormat = true;
        row.Shading.Color = Md(SurfaceSunken);
        row.Format.Font.Bold = true;
        row.Format.Font.Size = 9;
        for (var i = 0; i < labels.Length; i++)
            row.Cells[i].AddParagraph(labels[i]);
    }

    private static void HairlineBelow(Row row)
    {
        row.Borders.Bottom.Width = Unit.FromPoint(0.5);
        row.Borders.Bottom.Color = Md(Line);
    }

    // ---- Helpers ----

    private static XFont Font(string family, double size, bool bold = false) =>
        new(family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));

    private static MdColor Md(XColor c) => new(c.R, c.G, c.B);

    /// <summary>Draws one line with its top at <paramref name="top"/>; returns the top of the next line.</summary>
    private static double TextLine(XGraphics gfx, string text, XFont font, XBrush brush, double top, double x = Margin)
    {
        gfx.DrawString(text, font, brush, new XRect(x, top, Right - x, font.GetHeight()), XStringFormats.TopLeft);
        return top + font.GetHeight();
    }

    private static List<string> Wrap(XGraphics gfx, string text, XFont font, double width)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' '))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && gfx.MeasureString(candidate, font).Width > width)
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
    private static string Fit(XGraphics gfx, string text, XFont font, double width)
    {
        if (gfx.MeasureString(text, font).Width <= width)
            return text;
        for (var n = text.Length - 1; n > 0; n--)
        {
            var shorter = text[..n].TrimEnd() + "…";
            if (gfx.MeasureString(shorter, font).Width <= width)
                return shorter;
        }
        return "…";
    }

    /// <summary>ICU puts a narrow no-break space before "AM"; the report's fonts have no glyph for it.</summary>
    private static string Printable(string text) => text.Replace('\u202F', ' ');

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
