using System.Diagnostics;
using System.Globalization;
using System.Text;
using BuzzahBuddy.Models;
using BuzzahBuddy.Services.Reports;
using BuzzahBuddy.Services.Storage;
using PdfSharp.Pdf.IO;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

public class ReportPdfTests
{
    private static readonly DateOnly Today = new(2026, 10, 29);
    private static readonly DateOnly From = new(2026, 9, 30);
    private static readonly DateTimeOffset Created = new(2026, 10, 29, 12, 0, 0, TimeSpan.FromHours(-7));
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

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
        ReportPdf.Render(HistoryReport.Build(data, from, Today, Created), Assets(), Inv);

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
}
