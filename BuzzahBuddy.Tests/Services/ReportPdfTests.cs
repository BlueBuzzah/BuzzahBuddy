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

    private static ReportModel Model(HistoryData data, DateOnly from, bool comparePrevious = true, bool sample = false) =>
        HistoryReport.Build(data, from, Today, Created, comparePrevious, sample);

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

    private static readonly string WideNote = string.Concat(Enumerable.Repeat("WWWWWWWWW ", 25));
    private static readonly string CapsNote = string.Concat(Enumerable.Repeat("TREMOR WAS WORSE THIS MORNING AND WRITING WAS HARD ", 5));
    private static readonly string UnbrokenNote = "https://" + new string('w', 300);

    /// <summary>Eight profiles in October, so the Profiles section is at its tallest.</summary>
    private static HistoryData WithProfiles(HistoryData data)
    {
        for (var p = 0; p < 8; p++)
            data.Sessions.Add(Rec($"p{p}", new DateTimeOffset(2026, 10, 1 + p, 9, 0, 0, TimeSpan.FromHours(-7)), 600, $"Custom Profile With a Long Name {p}", SessionOutcome.EndedEarly));
        return data;
    }

    /// <summary>The three newest notes, newest first.</summary>
    private static HistoryData WithNotes(HistoryData data, params string[] texts)
    {
        for (var d = 0; d < texts.Length; d++)
            data.Notes.Add(new DayNote { Day = Today.AddDays(-d), Text = texts[d] });
        return data;
    }

    /// <summary>Everything at its longest: 24 months of rows, 8 profiles, long notes including an unbroken link.</summary>
    private static HistoryData Crowded() => WithNotes(WithProfiles(TwoYears()), WideNote, UnbrokenNote, WideNote);

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

    [Fact]
    public void Render_Captions()
    {
        var page1 = string.Join(" ", ReportPdf.Draw(Model(Fixture(), From), Assets()).PageText[0]);
        var page2 = string.Join(" ", ReportPdf.Draw(Model(Fixture(), From), Assets()).PageText[1]);
        var empty = string.Join(" ", ReportPdf.Draw(Model(new HistoryData(), From), Assets()).PageText[1]);

        Assert.Contains("Average time per day. Days without a session count as zero. The dashed line is the schedule early studies used (Pfeifer et al. 2021).", page1);
        // 08:00 and 09:00 starts are mornings; 14:00 isn't.
        Assert.Contains("Session starts by hour. Mornings 67%, afternoons and evenings 33%.", page2);
        Assert.Contains("Session starts by hour.", empty);
        Assert.DoesNotContain("Mornings", empty);
    }

    private static readonly string[] LayoutNames = ["fixture", "realistic", "empty", "one day", "two years", "crowded", "wide notes", "unbroken note", "all caps"];

    /// <summary>Every layout, as a person's report and as Demo Mode's sample (one more line on page 1).</summary>
    public static TheoryData<string, bool> Layouts
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var name in LayoutNames)
            {
                data.Add(name, false);
                data.Add(name, true);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Render_NothingOverflowsOrOverlaps(string name, bool sample)
    {
        // The note rows start from a base with no notes, so their three notes are the three newest.
        var (data, from, notes) = name switch
        {
            "fixture" => (Fixture(), From, null),
            "realistic" => (Realistic(), new DateOnly(2026, 8, 1), null),
            "empty" => (new HistoryData(), From, null),
            "one day" => (Fixture(), Today, null),
            "wide notes" => (WithNotes(WithProfiles(NoNotes(Fixture())), WideNote, WideNote, WideNote), From, new[] { WideNote, WideNote, WideNote }),
            "unbroken note" => (WithNotes(NoNotes(Fixture()), "Short one.", UnbrokenNote, "Another."), From, new[] { "Short one.", UnbrokenNote, "Another." }),
            "all caps" => (WithNotes(WithProfiles(NoNotes(Fixture())), CapsNote, CapsNote, CapsNote), From, new[] { CapsNote, CapsNote, CapsNote }),
            _ => (name == "crowded" ? Crowded() : TwoYears(), Today.AddDays(-729), (string[]?)null),
        };

        var layout = ReportPdf.Draw(Model(data, from, sample: sample), Assets());

        Assert.Equal(sample, layout.PageText[0].Contains(ReportPdf.SampleMarker));
        Assert.True(layout.Page1Bottom <= ReportPdf.BodyBottom, $"Page 1 ends at {layout.Page1Bottom}");
        AssertNothingOverlaps(layout);
        if (notes is not null)
            AssertNewestQuotesShown(layout, notes);
    }

    [Theory]
    [InlineData("phone")]
    [InlineData("iPad")]
    [InlineData("tablet")]
    public void Render_NamesTheDeviceTheReportWasRecordedOn(string noun)
    {
        var model = HistoryReport.Build(Fixture(), From, Today, Created, comparePrevious: true, device: noun);
        var layout = ReportPdf.Draw(model, Assets());

        var text = string.Join(" ", layout.PageText.SelectMany(p => p));
        Assert.Contains($"recorded on this {noun}.", text);
        Assert.Contains($"while this {noun} wasn't connected", text);
        AssertNothingOverlaps(layout);
        Assert.True(layout.Page1Bottom <= ReportPdf.BodyBottom);
    }

    [Theory]
    [InlineData(ReportRange.Last30Days)]
    [InlineData(ReportRange.Last90Days)]
    [InlineData(ReportRange.AllTime)]
    public void Render_DemoSample_MarkedAndFits(ReportRange range)
    {
        var data = DemoHistory.Build(new DateTimeOffset(Today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-7)));
        var (from, to) = HistoryReport.Range(range, Today, data.Sessions);
        var model = HistoryReport.Build(data, from, to, Created, comparePrevious: range != ReportRange.AllTime, sample: true);

        var layout = ReportPdf.Draw(model, Assets());

        Assert.True(model.Sample);
        Assert.Contains(ReportPdf.SampleMarker, layout.PageText[0]);
        Assert.Contains(layout.PageText[1], l => l.Contains(ReportPdf.SampleMarker, StringComparison.Ordinal));
        Assert.True(layout.Page1Bottom <= ReportPdf.BodyBottom, $"Page 1 ends at {layout.Page1Bottom}");
        AssertNothingOverlaps(layout);
    }

    [Fact]
    public void Render_RealHistory_NotMarked()
    {
        var layout = ReportPdf.Draw(Model(Fixture(), From), Assets());
        using var doc = Open(layout.Pdf);

        Assert.DoesNotContain(layout.PageText.SelectMany(p => p), l => l.Contains(ReportPdf.SampleMarker, StringComparison.Ordinal));
        Assert.Equal(ReportPdf.Title, doc.Info.Title);
    }

    [Fact]
    public void Render_Sample_TitleSaysSample()
    {
        using var doc = Open(ReportPdf.Render(Model(Fixture(), From, sample: true), Assets()));

        Assert.Equal("BuzzahBuddy Session Report (Sample)", doc.Info.Title);
    }

    private static HistoryData NoNotes(HistoryData data)
    {
        data.Notes.Clear();
        return data;
    }

    /// <summary>
    /// The quotes shown are the newest of <paramref name="notes"/> (dated today, yesterday, …), in
    /// order: all of them, or fewer when the oldest were dropped to fit; the count covers them all.
    /// </summary>
    private static void AssertNewestQuotesShown(ReportPdf.Layout layout, string[] notes)
    {
        var page2 = layout.PageText[1];
        var openings = page2.Where(l => l.StartsWith("\u201c", StringComparison.Ordinal)).ToList();
        var shown = layout.Boxes.Count(b => b.Kind == "date");

        Assert.InRange(shown, 1, notes.Length);
        Assert.Equal(shown, openings.Count);
        for (var i = 0; i < shown; i++)
        {
            Assert.StartsWith("\u201c" + notes[i][..6], openings[i]);
            var date = Today.AddDays(-i).ToString("MMM d", CultureInfo.InvariantCulture);
            Assert.Contains(page2, l => l == date || l.EndsWith("  " + date, StringComparison.Ordinal));
        }
        Assert.Contains($"{notes.Length} notes in this period.", page2);
    }

    /// <summary>
    /// Recent Notes ends above the legal text, every quote line ends before the date set after it,
    /// and every profile name ends before its bar.
    /// </summary>
    private static void AssertNothingOverlaps(ReportPdf.Layout layout)
    {
        Assert.True(layout.NotesBottom <= layout.LegalTop, $"Notes end at {layout.NotesBottom}, legal text starts at {layout.LegalTop}");
        foreach (var date in layout.Boxes.Where(b => b.Kind == "date"))
            foreach (var quote in layout.Boxes.Where(b => b.Kind == "quote" && b.Row == date.Row))
                Assert.True(quote.Rect.Right <= date.Rect.Left, $"Quote ends at {quote.Rect.Right}, its date starts at {date.Rect.Left}");
        foreach (var name in layout.Boxes.Where(b => b.Kind == "name"))
        {
            var track = layout.Boxes.Single(b => b.Kind == "track" && b.Row == name.Row);
            Assert.True(name.Rect.Right <= track.Rect.Left, $"Name ends at {name.Rect.Right}, its bar starts at {track.Rect.Left}");
        }
        foreach (var quote in layout.Boxes.Where(b => b.Kind == "quote"))
            Assert.True(quote.Rect.Right <= 612 - 54, $"Quote line ends at {quote.Rect.Right}");
    }

    [Fact]
    public void Render_LongNote_CappedAtThreeLines()
    {
        // 200 W's split into 40-character runs wrap to 5 lines uncapped.
        var layout = ReportPdf.Draw(Model(WithNotes(new HistoryData(), new string('W', 200)), From), Assets());

        Assert.Equal(3, layout.Boxes.Count(b => b.Kind == "quote"));
        // Not cut by the 200-character limit, so the ellipsis is the 3-line cap's.
        Assert.Contains(layout.PageText[1], l => l.Contains("\u2026\u201d", StringComparison.Ordinal));
        AssertNothingOverlaps(layout);
    }

    [Fact]
    public void Render_LongNotes_OldestDroppedWhenTheyStillDoNotFit()
    {
        var w = new string('W', 200);
        var layout = ReportPdf.Draw(Model(WithNotes(WithProfiles(TwoYears()), w, w, w), Today.AddDays(-729)), Assets());
        var dates = layout.Boxes.Where(b => b.Kind == "date").ToList();

        // The newest quotes stay; the count still covers every note.
        Assert.InRange(dates.Count, 1, 2);
        Assert.Contains(layout.PageText[1], l => l.EndsWith("Oct 29", StringComparison.Ordinal));
        Assert.DoesNotContain(layout.PageText[1], l => l.EndsWith("Oct 27", StringComparison.Ordinal));
        Assert.Contains("3 notes in this period.", layout.PageText[1]);
        AssertNothingOverlaps(layout);
    }

    [Fact]
    public void Render_UnsupportedCharacters_DrawAsQuestionMarks()
    {
        const string note = "Felt great \U0001F60A \u4ECA\u65E5\u306F\u826F\u3044 \u00DCn\u00EFc\u00F8d\u00E9 \u2014 test";
        const string profile = "Night \U0001F319 \u041F\u0440\u043E\u0444\u0438\u043B\u044C";
        var data = Fixture();
        data.Notes.Add(new DayNote { Day = Today, Text = note });
        data.Sessions.Add(Rec("n", new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.FromHours(-7)), 3600, profile, SessionOutcome.Completed));

        var layout = ReportPdf.Draw(Model(data, From), Assets());
        var page2 = layout.PageText[1];

        var quote = Assert.Single(page2, l => l.StartsWith("\u201cFelt great", StringComparison.Ordinal));
        Assert.StartsWith("\u201cFelt great ? ????? \u00DCn\u00EFc\u00F8d\u00E9 \u2014 test\u201d", quote);
        var name = Assert.Single(page2, l => l.StartsWith("Night ", StringComparison.Ordinal));
        Assert.StartsWith("Night ? ", name);
        Assert.All(quote.EnumerateRunes().Concat(name.EnumerateRunes()), r => Assert.True(ReportPdf.CanRender(r), $"U+{r.Value:X4} has no glyph"));
        AssertNothingOverlaps(layout);
        // The CSV keeps the exact text.
        var csv = HistoryReport.Csv(data.Sessions, data.Notes);
        Assert.Contains(note, csv);
        Assert.Contains(profile, csv);
    }

    [Fact]
    public void Render_FirstSessionCaption_OnlyWhenTheFirstSessionIsInsideThePeriod()
    {
        var sinceFirst = ReportPdf.Draw(Model(Fixture(), From), Assets()).PageText[0];
        var data = Fixture();
        data.Sessions.Add(Rec("early", new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.FromHours(-7)), 3600, "Noisy", SessionOutcome.Completed));
        var whole = ReportPdf.Draw(Model(data, From), Assets()).PageText[0];

        Assert.Contains("First session Oct 20, 2026.", sinceFirst);
        Assert.DoesNotContain(whole, l => l.StartsWith("First session", StringComparison.Ordinal));
        // The trend axis ends on the period's last day.
        Assert.Contains("Oct 29", whole);
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
