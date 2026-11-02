using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

/// <summary>
/// The app's Info.plist is what runs on the phone; BuzzahBuddyKit's copy only feeds Siri
/// phrase training. Their INAlternativeAppNames must stay identical, hints included, and the
/// framework's display name must be the app's title.
/// </summary>
public class AlternativeAppNamesTests
{
    private static readonly string PlistDir = Path.Combine(AppContext.BaseDirectory, "Plists");

    [Fact]
    public void AppAndBuzzahBuddyKitPlists_ListTheSameAlternativeAppNames()
    {
        var app = AlternativeAppNames(Path.Combine(PlistDir, "App.Info.plist"));
        var kit = AlternativeAppNames(Path.Combine(PlistDir, "BuzzahBuddyKit.Info.plist"));

        Assert.Equal(new[]
        {
            "INAlternativeAppName=Glove",
            "INAlternativeAppName=Gloves",
            "INAlternativeAppName=BlueBuzzah;INAlternativeAppNamePronunciationHint=blue buzzah",
        }, app);
        Assert.Equal(app, kit);
    }

    // Siri says the framework's display name in phrases; the phone shows the app's ApplicationTitle.
    [Fact]
    public void BuzzahBuddyKitDisplayName_IsTheAppTitle()
    {
        var title = XDocument.Load(Path.Combine(PlistDir, "BuzzahBuddy.csproj"))
            .Descendants("ApplicationTitle").Single().Value;

        Assert.Equal(title, PlistString(Path.Combine(PlistDir, "BuzzahBuddyKit.Info.plist"), "CFBundleDisplayName"));
    }

    // Siri's phrases are trained for the development region's language only.
    [Fact]
    public void AppPlist_HasEnglishDevelopmentRegion()
    {
        Assert.Equal("en", PlistString(Path.Combine(PlistDir, "App.Info.plist"), "CFBundleDevelopmentRegion"));
    }

    private static XDocument LoadPlist(string path)
    {
        // Skip the plist DOCTYPE: XmlReaderSettings prohibits DTDs by default
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        return XDocument.Load(reader);
    }

    private static string PlistString(string path, string key)
    {
        var value = LoadPlist(path).Descendants("key").Single(k => k.Value == key).ElementsAfterSelf().First();
        Assert.Equal("string", value.Name.LocalName);
        return value.Value;
    }

    /// <summary>Each entry as its key=value pairs in file order, e.g. "INAlternativeAppName=Glove".</summary>
    private static List<string> AlternativeAppNames(string path)
    {
        var key = LoadPlist(path).Descendants("key").Single(k => k.Value == "INAlternativeAppNames");
        var array = key.ElementsAfterSelf().First();
        Assert.Equal("array", array.Name.LocalName);

        return array.Elements("dict")
            .Select(dict => string.Join(";", dict.Elements("key").Select(k => $"{k.Value}={k.ElementsAfterSelf().First().Value}")))
            .ToList();
    }
}
