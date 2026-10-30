using System.Diagnostics;
using System.Globalization;
using System.Text;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Reports;
using BuzzahBuddy.Services.Storage;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using PdfSharp.Pdf.IO;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public class ReportPdfTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateOnly From = new(2026, 9, 30);
    private static readonly DateTimeOffset Created = new(2026, 10, 29, 12, 0, 0, TimeSpan.FromHours(-7));

    private static readonly string AssetDir = Path.Combine(AppContext.BaseDirectory, "ReportAssets");

    /// <summary>The same files the app embeds, read from the test output instead of the app assembly.</summary>
    private static ReportAssets Assets() => new(
        face => File.ReadAllBytes(Path.Combine(AssetDir, face + ".ttf")),
        File.ReadAllBytes(Path.Combine(AssetDir, "bluebuzzah-mark.png")));

    private static SessionRecord Rec(string id, DateTimeOffset start, int seconds, string profile, SessionOutcome outcome) => new()
    {
        Id = id,
        StartedAt = start,
        EndedAt = start.AddSeconds(seconds),
        TherapySeconds = seconds,
        PlannedSeconds = 7200,
        ProfileName = profile,
        Outcome = outcome,
    };

    private static HistoryData Fixture()
    {
        var offset = TimeSpan.FromHours(-7);
        return new HistoryData
        {
            Sessions =
            {
                Rec("a", new DateTimeOffset(2026, 10, 28, 8, 0, 0, offset), 7200, "Noisy", SessionOutcome.Completed),
                Rec("b", new DateTimeOffset(2026, 10, 28, 14, 0, 0, offset), 7200, "Noisy", SessionOutcome.Completed),
                Rec("c", new DateTimeOffset(2026, 10, 20, 9, 0, 0, offset), 3600, "Regular", SessionOutcome.StoppedEarly),
            },
            Notes = { new DayNote { Day = new DateOnly(2026, 10, 28), Text = "Tremor felt lighter after lunch" } },
        };
    }

    private static byte[] Render(HistoryData data, DateOnly from) =>
        ReportPdf.Render(HistoryReport.Build(data, from, Today, Created), Assets());

    private static Document Flow(HistoryData data) =>
        ReportPdf.FlowDocument(HistoryReport.Build(data, From, Today, Created), File.ReadAllBytes(Path.Combine(AssetDir, "bluebuzzah-mark.png")));

    /// <summary>The text MigraDoc will set for a paragraph, cell or run, with line breaks as \n.</summary>
    private static string TextOf(DocumentObject? obj) => obj switch
    {
        Paragraph p => string.Concat(p.Elements.OfType<DocumentObject>().Select(TextOf)),
        FormattedText f => string.Concat(f.Elements.OfType<DocumentObject>().Select(TextOf)),
        Cell c => string.Concat(c.Elements.OfType<DocumentObject>().Select(TextOf)),
        Text t => t.Content,
        Character { SymbolName: SymbolName.LineBreak } => "\n",
        Character { SymbolName: SymbolName.Blank } ch => new string(' ', Math.Max(1, ch.Count)),
        _ => "",
    };

    private static Table DailyLog(Document doc) =>
        doc.Sections[0]!.Elements.OfType<Table>().Single(t => TextOf(t.Rows[0].Cells[0]) == "Date");

    private static List<Row> BodyRows(Table table) =>
        Enumerable.Range(1, table.Rows.Count - 1).Select(i => table.Rows[i]).ToList();

    [Fact]
    public void Render_ProducesTitledPdfWithEmbeddedFonts()
    {
        var bytes = Render(Fixture(), From);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.Equal(ReportPdf.Title, doc.Info.Title);
        Assert.True(doc.PageCount >= 2);
        // Font names come from the TTFs' own name tables; PDF names write a space as #20.
        var raw = Encoding.Latin1.GetString(bytes);
        Assert.Contains("+Lexend", raw);
        Assert.Contains("+Atkinson#20Hyperlegible#20Next", raw);
    }

    [Fact]
    public void Render_EmptyPeriod_StillRenders()
    {
        var bytes = Render(new HistoryData(), From);

        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(doc.PageCount >= 2);
    }

    [Fact]
    public void Render_TwoYearsOfHistory_Paginates()
    {
        var data = new HistoryData();
        var first = Today.AddDays(-729);
        for (var i = 0; i < 730; i++)
        {
            var day = first.AddDays(i).ToDateTime(TimeOnly.MinValue);
            var start = new DateTimeOffset(day, TimeSpan.FromHours(-7));
            data.Sessions.Add(Rec($"m{i}", start.AddHours(8), 7200, "Noisy", SessionOutcome.Completed));
            data.Sessions.Add(Rec($"e{i}", start.AddHours(15), 5400, "Regular", SessionOutcome.StoppedEarly));
        }

        var watch = Stopwatch.StartNew();
        var bytes = Render(data, first);
        watch.Stop();

        using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
        Assert.True(doc.PageCount > 10);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Rendering took {watch.Elapsed}");
    }

    [Fact]
    public void Render_FromEmbeddedAssets_LoadsEveryFace()
    {
        // FromEmbedded(typeof(App).Assembly) needs the app assembly; check that the files it
        // reads are exactly the six this project links from the app's Resources.
        var linked = Directory.GetFiles(AssetDir).Select(Path.GetFileName).Order(StringComparer.Ordinal);

        Assert.Equal(linked, ReportAssets.ResourceFiles.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FlowDocument_DayTooLongForOnePage_FlowsInsteadOfBeingKeptTogether()
    {
        var data = new HistoryData();
        var start = new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.FromHours(-7));
        for (var i = 0; i < 80; i++)
            data.Sessions.Add(Rec($"s{i}", start.AddMinutes(i * 15), 600, "Noisy", SessionOutcome.Completed));

        var rows = BodyRows(DailyLog(Flow(data)));

        Assert.Equal(80, rows.Count(r => TextOf(r.Cells[4]) == "Completed"));
        // No day keeps more than 15 rows together; a longer one flows across pages.
        Assert.All(rows, r => Assert.True(r.KeepWith + 1 <= 15, $"KeepWith {r.KeepWith}"));
    }

    [Fact]
    public void FlowDocument_ShortDay_IsKeptTogether()
    {
        var rows = BodyRows(DailyLog(Flow(Fixture())));

        // 10-20: one session. 10-28: two sessions and the note row.
        Assert.Equal(0, rows[0].KeepWith);
        Assert.Equal(2, rows[1].KeepWith);
    }

    [Fact]
    public void FlowDocument_LongNote_SplitsIntoRowsOfFourLines()
    {
        var lines = Enumerable.Range(1, 120).Select(n => $"Line {n}").ToList();
        var data = new HistoryData { Notes = { new DayNote { Day = Today, Text = string.Join("\n", lines) } } };

        var noteRows = BodyRows(DailyLog(Flow(data))).Where(r => r.Cells[0].MergeRight == 4).Select(r => TextOf(r.Cells[0])).ToList();

        Assert.Equal(30, noteRows.Count);
        Assert.StartsWith("Note: Line 1\n", noteRows[0]);
        Assert.All(noteRows.Skip(1), t => Assert.DoesNotContain("Note:", t));
        Assert.All(noteRows, t => Assert.True(t.Split('\n').Length <= 4));
        Assert.Equal("Note: " + string.Join("\n", lines), string.Join("\n", noteRows));
    }

    [Fact]
    public void FlowDocument_HasBothLegalNoticesVerbatim()
    {
        var paragraphs = Flow(Fixture()).Sections[0]!.Elements.OfType<Paragraph>().Select(p => TextOf(p)).ToList();

        Assert.Contains(LegalNotices.MedicalDisclaimer, paragraphs);
        Assert.Contains(LegalNotices.ImplantedDeviceNotice, paragraphs);
    }

    [Fact]
    public void Report_UnderJapaneseCulture_FormatsDatesAndTimesInEnglish()
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");

            var cells = BodyRows(DailyLog(Flow(Fixture()))).SelectMany(r => Enumerable.Range(0, 5).Select(i => TextOf(r.Cells[i]))).ToList();
            var weeks = Flow(Fixture()).Sections[0]!.Elements.OfType<Table>().First();
            using var doc = PdfReader.Open(new MemoryStream(Render(Fixture(), From)), PdfDocumentOpenMode.Import);

            Assert.Contains("Oct 20, 2026", cells);
            Assert.Contains("9:00 AM", cells);
            Assert.Equal("Oct 16 to Oct 22", TextOf(weeks.Rows[weeks.Rows.Count - 1 - 1].Cells[0]));
            Assert.Equal("Sep 30, 2026 to Oct 29, 2026", doc.Info.Subject);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }
}
