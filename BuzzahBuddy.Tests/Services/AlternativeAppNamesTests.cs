using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace BuzzahBuddy.Tests.Services;

/// <summary>
/// The app's Info.plist is what runs on the phone; BuzzahBuddyKit's copy only feeds Siri
/// phrase training. Their INAlternativeAppNames must stay identical, hints included.
/// </summary>
public class AlternativeAppNamesTests
{
    private static readonly string PlistDir = Path.Combine(AppContext.BaseDirectory, "Plists");

    [Fact]
    public void AppAndBuzzahBuddyKitPlists_ListTheSameAlternativeAppNames()
    {
        var app = AlternativeAppNames(Path.Combine(PlistDir, "App.Info.plist"));
        var kit = AlternativeAppNames(Path.Combine(PlistDir, "BuzzahBuddyKit.Info.plist"));

        Assert.NotEmpty(app);
        Assert.Equal(app, kit);
    }

    /// <summary>Each entry as its key=value pairs in file order, e.g. "INAlternativeAppName=Glove".</summary>
    private static List<string> AlternativeAppNames(string path)
    {
        // Skip the plist DOCTYPE: XmlReaderSettings prohibits DTDs by default
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        var key = XDocument.Load(reader).Descendants("key").Single(k => k.Value == "INAlternativeAppNames");
        var array = key.ElementsAfterSelf().First();
        Assert.Equal("array", array.Name.LocalName);

        return array.Elements("dict")
            .Select(dict => string.Join(";", dict.Elements("key").Select(k => $"{k.Value}={k.ElementsAfterSelf().First().Value}")))
            .ToList();
    }
}
