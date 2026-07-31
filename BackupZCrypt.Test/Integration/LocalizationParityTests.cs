using System.Xml.Linq;

using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Verifies that every message code has an English resx key and that the English and Spanish keys match.
/// These tests read the resource files themselves, which is what makes both key sets visible at once;
/// <c>MessageLocalizerTests</c> covers the complementary runtime question of whether the resources actually
/// resolve through the resource manager for the culture in effect.
/// </summary>
public sealed class LocalizationParityTests
{
    /// <summary>
    /// The path of the English resource file. The assertions compare the declared key sets of both
    /// languages, which the compiled resources cannot answer because they only expose the values of the
    /// culture currently in effect, so the resx XML is parsed instead — read from the copy the build places
    /// in the test output, since the tests run from that directory rather than from the repository.
    /// </summary>
    private static readonly string EnglishResxPath = Path.Combine(
        AppContext.BaseDirectory,
        "TestData",
        "Strings.resx"
    );

    /// <summary>
    /// The path of the Spanish resource file, copied into the test output alongside its English
    /// counterpart.
    /// </summary>
    private static readonly string SpanishResxPath = Path.Combine(
        AppContext.BaseDirectory,
        "TestData",
        "Strings.es.resx"
    );

    [Test]
    public void EveryMessageCode_HasEnglishResxKey()
    {
        var englishKeys = ReadResxKeys(EnglishResxPath);

        var missing = Enum.GetNames<MessageCode>()
            .Where(code => !englishKeys.Contains(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        Assert.That(
            missing,
            Is.Empty,
            "MessageCode members with no key in Strings.resx (would show the raw enum name): "
                + string.Join(", ", missing)
        );
    }

    [Test]
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

        Assert.That(
            onlyInEnglish.Count == 0 && onlyInSpanish.Count == 0,
            Is.True,
            "Strings.resx and Strings.es.resx must contain the identical set of keys.\n"
                + $"Only in English: {Describe(onlyInEnglish)}\n"
                + $"Only in Spanish: {Describe(onlyInSpanish)}"
        );
    }

    /// <summary>
    /// Reads the resource names declared by a resx file, failing the test with a hint about the
    /// output copy step when the file is absent.
    /// </summary>
    /// <param name="resxPath">The path of the resx file to parse.</param>
    /// <returns>The ordinal-compared set of resource names it declares.</returns>
    private static HashSet<string> ReadResxKeys(string resxPath)
    {
        Assert.That(
            File.Exists(resxPath),
            Is.True,
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

    /// <summary>
    /// Renders a set of resx keys so a parity failure names the offending entries.
    /// </summary>
    /// <param name="keys">The keys to list.</param>
    /// <returns>The comma-separated keys, or "(none)" when the list is empty.</returns>
    private static string Describe(List<string> keys)
    {
        return keys.Count == 0 ? "(none)" : string.Join(", ", keys);
    }
}
