using System.Diagnostics;
using System.Globalization;
using System.Text;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Reports;
using BuzzahBuddy.Services.Storage;
using PdfSharp.Pdf;
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

    private static ReportModel Model(HistoryData data, DateOnly from, bool comparePrevious = true) =>
        HistoryReport.Build(data, from, Today, Created, comparePrevious);

    private static byte[] Render(HistoryData data, DateOnly from) => ReportPdf.Render(Model(data, from), Assets());

    private static PdfDocument Open(byte[] bytes) => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

    /// <summary>Two sessions every day for two years, as in a long All Time report.</summary>
    private static HistoryData TwoYears()
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
        return data;
    }

    /// <summary>
    /// 90 days like the approved mockup's: about 1 day in 6 skipped, a 5-day gap in mid-September,
    /// a 2-hour morning session most days and an afternoon one more often as the weeks go on, mostly
    /// completed, two profiles, a few notes, and a previous 90 days with sessions so the tiles compare.
    /// </summary>
    internal static HistoryData Realistic()
    {
        var data = new HistoryData();
        var random = new Random(7);
        var offset = TimeSpan.FromHours(-7);
        var first = Today.AddDays(-179);

        void Add(string id, DateOnly day, int hour, string profile)
        {
            var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, random.Next(4) * 15)), offset);
            var roll = random.NextDouble();
            var (outcome, seconds) = roll < 0.06 ? (SessionOutcome.StoppedEarly, random.Next(30, 100) * 60)
                : roll < 0.1 ? (SessionOutcome.EndedEarly, random.Next(20, 110) * 60)
                : (SessionOutcome.Completed, 7200);
            data.Sessions.Add(Rec(id, start, seconds, profile, outcome));
        }

        for (var i = 0; i < 180; i++)
        {
            var day = first.AddDays(i);
            var inPeriod = i >= 90;
            var progress = inPeriod ? (i - 90) / 89.0 : 0;
            if (random.NextDouble() < (inPeriod ? 0.22 - 0.1 * progress : 0.3) || day >= new DateOnly(2026, 9, 15) && day <= new DateOnly(2026, 9, 19))
                continue;
            var afternoonChance = inPeriod ? 0.4 + 0.55 * progress : 0.5;
            Add($"a{i}", day, 7 + random.Next(3), "Noisy");
            if (random.NextDouble() < afternoonChance)
                Add($"b{i}", day, 13 + random.Next(6), random.NextDouble() < 0.3 ? "Regular" : "Noisy");
        }

        var notes = new (int Month, int Day, string Text)[]
        {
            (8, 3, "First week with both gloves."),
            (8, 12, "Hands felt warm after the morning session."),
            (8, 20, "Forgot the afternoon session."),
            (8, 29, "Long drive, only did the morning."),
            (9, 6, "Tremor felt about the same today."),
            (9, 14, "Travelling until Saturday, gloves stayed home."),
            (9, 21, "Back to the usual routine."),
            (9, 30, "Buttoning a shirt felt a little easier."),
            (10, 4, "Tired in the evening."),
            (10, 9, "Doctor visit next month."),
            (10, 12, "Writing was easier today."),
            (10, 21, "Skipped the evening session, too tired."),
            (10, 28, "Tremor felt lighter after lunch."),
        };
        foreach (var n in notes)
            data.Notes.Add(new DayNote { Day = new DateOnly(2026, n.Month, n.Day), Text = n.Text });
        return data;
    }

    /// <summary>Everything at its longest: 24 months of rows, 8 profiles, three 200-character notes.</summary>
    private static HistoryData Crowded()
    {
        var data = TwoYears();
        for (var p = 0; p < 8; p++)
            data.Sessions.Add(Rec($"p{p}", new DateTimeOffset(2026, 10, 1 + p, 9, 0, 0, TimeSpan.FromHours(-7)), 600, $"Custom Profile With a Long Name {p}", SessionOutcome.EndedEarly));
        for (var d = 0; d < 3; d++)
            data.Notes.Add(new DayNote { Day = Today.AddDays(-d), Text = string.Concat(Enumerable.Repeat("Wide words WWWWWWWW MMMMMMMM ", 12)) });
        data.Notes.Add(new DayNote { Day = Today.AddDays(-3), Text = "https://" + new string('w', 300) });
        return data;
    }

    [Fact]
    public void Render_TwoPagesTitledEmbedded()
    {
        var bytes = Render(Fixture(), From);

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        using var doc = Open(bytes);
        Assert.Equal(2, doc.PageCount);
        Assert.Equal(ReportPdf.Title, doc.Info.Title);
        // Font names come from the TTFs' own name tables; PDF names write a space as #20.
        var raw = Encoding.Latin1.GetString(bytes);
        Assert.Contains("+Lexend", raw);
        Assert.Contains("+Atkinson#20Hyperlegible#20Next", raw);
    }

    [Fact]
    public void Render_EmptyAndTwoYears()
    {
        using (var empty = Open(Render(new HistoryData(), From)))
            Assert.Equal(2, empty.PageCount);

        var data = TwoYears();
        var watch = Stopwatch.StartNew();
        var bytes = ReportPdf.Render(Model(data, Today.AddDays(-729), comparePrevious: false), Assets());
        watch.Stop();

        using var doc = Open(bytes);
        Assert.Equal(2, doc.PageCount);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Rendering took {watch.Elapsed}");
    }

    /// <summary>
    /// PDFsharp can't extract text it wrote with Unicode glyph IDs, so this reads the lines the
    /// renderer drew on page 2, recorded during the same render that produced the PDF.
    /// </summary>
    [Fact]
    public void Render_ContainsLegalNotices()
    {
        var page2 = string.Join(" ", ReportPdf.Draw(Model(Fixture(), From), Assets()).PageText[1]);

        Assert.Contains(LegalNotices.MedicalDisclaimer, page2);
        Assert.Contains(LegalNotices.ImplantedDeviceNotice, page2);
    }

    public static TheoryData<string> Layouts => new() { "fixture", "realistic", "empty", "one day", "two years", "crowded" };

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Render_NothingOverflowsOrOverlaps(string name)
    {
        var (data, from) = name switch
        {
            "fixture" => (Fixture(), From),
            "realistic" => (Realistic(), new DateOnly(2026, 8, 1)),
            "empty" => (new HistoryData(), From),
            "one day" => (Fixture(), Today),
            _ => (name == "crowded" ? Crowded() : TwoYears(), Today.AddDays(-729)),
        };

        var layout = ReportPdf.Draw(Model(data, from), Assets());

        Assert.True(layout.Page1Bottom <= ReportPdf.BodyBottom, $"Page 1 ends at {layout.Page1Bottom}");
        Assert.True(layout.NotesBottom <= layout.LegalTop, $"Notes end at {layout.NotesBottom}, legal text starts at {layout.LegalTop}");
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
    public void BreakLongWords_SplitsUnbrokenRunsSoTheyWrap()
    {
        // A pasted link: the report wraps only at spaces, so an unbroken run would leave the page.
        var word = "https://" + string.Concat(Enumerable.Range(0, 990).Select(i => (char)('a' + i % 26)));

        var text = ReportPdf.BreakLongWords(word);

        Assert.All(text.Split(' '), w => Assert.True(w.Length <= 40, $"{w.Length}-character word"));
        Assert.Equal(word, text.Replace(" ", ""));
    }

    [Fact]
    public void Report_UnderJapaneseCulture_FormatsDatesInEnglish()
    {
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");

            var layout = ReportPdf.Draw(Model(Fixture(), From), Assets());
            using var doc = Open(layout.Pdf);

            Assert.Contains("Sep 30, 2026 to Oct 29, 2026 · 30 days", layout.PageText[0]);
            Assert.Contains(layout.PageText[1], line => line.EndsWith("Oct 28", StringComparison.Ordinal));
            Assert.Equal("Sep 30, 2026 to Oct 29, 2026", doc.Info.Subject);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }
}
