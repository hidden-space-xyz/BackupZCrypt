using System.Xml.Linq;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Integration;

// Guards the documented silent-fail footgun: a MessageCode with no matching resx entry, or
// a key present in only one language, shows the raw key/enum name in the UI at runtime.
// The Desktop resx files are copied to the test output under TestData/ by the csproj.
public sealed class LocalizationParityTests
{
    private static readonly string EnglishResxPath = Path.Combine(
        AppContext.BaseDirectory,
        "TestData",
        "Strings.resx"
    );

    private static readonly string SpanishResxPath = Path.Combine(
        AppContext.BaseDirectory,
        "TestData",
        "Strings.es.resx"
    );

    [Fact]
    public void EveryMessageCode_HasEnglishResxKey()
    {
        var englishKeys = ReadResxKeys(EnglishResxPath);

        var missing = Enum.GetNames<MessageCode>()
            .Where(code => !englishKeys.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"MessageCode members with no key in Strings.resx (would show the raw enum name): "
                + string.Join(", ", missing)
        );
    }

    [Fact]
    public void EnglishAndSpanish_HaveIdenticalKeySets()
    {
        var englishKeys = ReadResxKeys(EnglishResxPath);
        var spanishKeys = ReadResxKeys(SpanishResxPath);

        var onlyInEnglish = englishKeys
            .Except(spanishKeys)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        var onlyInSpanish = spanishKeys
            .Except(englishKeys)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            onlyInEnglish.Count == 0 && onlyInSpanish.Count == 0,
            "Strings.resx and Strings.es.resx must contain the identical set of keys.\n"
                + $"Only in English: {Describe(onlyInEnglish)}\n"
                + $"Only in Spanish: {Describe(onlyInSpanish)}"
        );
    }

    private static HashSet<string> ReadResxKeys(string resxPath)
    {
        Assert.True(
            File.Exists(resxPath),
            $"Resx file not found at '{resxPath}'. Confirm it is copied to the test output."
        );

        var document = XDocument.Load(resxPath);

        return document
            .Root!.Elements("data")
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string Describe(IReadOnlyCollection<string> keys)
    {
        return keys.Count == 0 ? "(none)" : string.Join(", ", keys);
    }
}
