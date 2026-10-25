using System.Globalization;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Desktop.Presentation;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Desktop.Presentation;

/// <summary>
/// Unit tests for <see cref="MessageLocalizer"/>, the single point where the language-neutral codes the
/// lower layers emit become text the user reads. Every case is driven from <c>Enum.GetValues</c> and from
/// the placeholders the resources themselves declare, so a code added without a resource — which the app
/// would silently render as the raw enum name — fails here rather than in front of a user.
/// </summary>
public sealed class MessageLocalizerTests
{
    /// <summary>
    /// The suffix that, by convention, marks a code whose resource consumes <c>string.Format</c> arguments.
    /// </summary>
    private const string FormatSuffix = "Format";

    [Fact]
    internal void Localize_EveryMessageCode_ResolvesToLocalizedTextRatherThanTheCodeName()
    {
        var offenders = new List<string>();

        foreach (var code in Enum.GetValues<MessageCode>())
        {
            var name = code.ToString();
            var text = MessageLocalizer.Localize(new LocalizableMessage(code, ArgumentsFor(name)));

            if (string.IsNullOrWhiteSpace(text) || string.Equals(text, name, StringComparison.Ordinal))
            {
                offenders.Add(name);
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    internal void Localize_EveryCodeNamedFormat_SubstitutesEveryArgumentItWasGiven()
    {
        var offenders = new List<string>();

        foreach (var code in FormatCodes())
        {
            var name = code.ToString();
            var format = Strings.GetByKey(name);
            var arguments = ArgumentsFor(name);

            if (arguments.Length is 0)
            {
                offenders.Add($"{name} (named Format but its resource declares no placeholder)");
                continue;
            }

            var text = MessageLocalizer.Localize(new LocalizableMessage(code, arguments));

            if (string.Equals(text, format, StringComparison.Ordinal))
            {
                offenders.Add($"{name} (the arguments were not applied at all)");
            }
            else if (Array.Exists(arguments, argument => !text.Contains((string)argument, StringComparison.Ordinal)))
            {
                offenders.Add($"{name} (dropped an argument, rendering '{text}')");
            }
            else if (PlaceholderIndexes(text).Any())
            {
                offenders.Add($"{name} (left an unsubstituted placeholder in '{text}')");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    internal void Localize_EveryCodeNotNamedFormat_RendersWithoutLeavingAPlaceholder()
    {
        var offenders = Enum.GetValues<MessageCode>()
            .Where(static code =>
                !code.ToString().EndsWith(FormatSuffix, StringComparison.Ordinal)
                && PlaceholderIndexes(MessageLocalizer.Localize(new LocalizableMessage(code))).Any()
            )
            .Select(static code => code.ToString())
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    internal void Localize_CodeWithNoMatchingResource_FallsBackToTheCodeItselfInsteadOfThrowing()
    {
        var unmapped = (MessageCode)(-1);

        Assert.Multiple(
            () => Assert.Equal("-1", MessageLocalizer.Localize(new LocalizableMessage(unmapped))),
            () => Assert.Equal("-1", MessageLocalizer.Localize(new LocalizableMessage(unmapped, "detail")))
        );
    }

    [Fact]
    internal void Localize_ArgumentThatIsItselfAMessage_IsLocalizedBeforeBeingInserted()
    {
        var text = MessageLocalizer.Localize(
            new LocalizableMessage(
                MessageCode.FileBackupErrorFormat,
                "docs/a.txt",
                new LocalizableMessage(MessageCode.ReasonIoErrorFormat, "detail")
            )
        );

        var expected = string.Format(
            CultureInfo.CurrentCulture,
            Strings.GetByKey(nameof(MessageCode.FileBackupErrorFormat)),
            "docs/a.txt",
            string.Format(
                CultureInfo.CurrentCulture,
                Strings.GetByKey(nameof(MessageCode.ReasonIoErrorFormat)),
                "detail"
            )
        );

        Assert.Equal(expected, text);
    }

    [Fact]
    [SetCulture("en-US")]
    internal void Localize_AmountOfStorage_IsWrittenWithASizeUnit()
    {
        var text = MessageLocalizer.Localize(
            new LocalizableMessage(MessageCode.LowDiskSpaceFormat, new ByteCount(1_000), new ByteCount(5_242_880))
        );

        var expected = string.Format(
            CultureInfo.CurrentCulture,
            Strings.GetByKey(nameof(MessageCode.LowDiskSpaceFormat)),
            "1000 B",
            "5.0 MB"
        );

        Assert.Equal(expected, text);
    }

    [Theory]
    [SetCulture("en-US")]
    [InlineData(new[] { "a.txt" }, "a.txt")]
    [InlineData(new[] { "a.txt", "b.txt" }, "a.txt, b.txt")]
    [InlineData(new[] { "1", "2", "3", "4", "5" }, "1, 2, 3, 4, 5")]
    [InlineData(new[] { "1", "2", "3", "4", "5", "6", "7" }, "1, 2, 3, 4, 5, …")]
    internal void Localize_SampleOfPaths_IsListedWithAnEllipsisWhenCutShort(string[] paths, string listed)
    {
        var text = MessageLocalizer.Localize(
            new LocalizableMessage(MessageCode.SourceLinksSkippedFormat, paths.Length, PathSample.Of(paths))
        );

        var expected = string.Format(
            CultureInfo.CurrentCulture,
            Strings.GetByKey(nameof(MessageCode.SourceLinksSkippedFormat)),
            paths.Length,
            listed
        );

        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Gets the codes that the naming convention marks as taking format arguments.
    /// </summary>
    /// <returns>Every <see cref="MessageCode"/> whose name ends in <c>Format</c>.</returns>
    private static IEnumerable<MessageCode> FormatCodes()
    {
        return Enum.GetValues<MessageCode>()
            .Where(static code => code.ToString().EndsWith(FormatSuffix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Builds one distinctly recognizable argument per placeholder the resource behind a key declares, so a
    /// message can be localized without the test restating the arity of each resource.
    /// </summary>
    /// <param name="key">The resource key, which is the message code's name.</param>
    /// <returns>One argument per placeholder index, or an empty array when the resource takes none.</returns>
    private static object[] ArgumentsFor(string key)
    {
        var highestIndex = PlaceholderIndexes(Strings.GetByKey(key))
            .Select(static index => int.Parse(index, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(-1)
            .Max();

        return
        [
            .. Enumerable.Range(0, highestIndex + 1)
                .Select(static index => (object)string.Create(CultureInfo.InvariantCulture, $"ARG{index}")),
        ];
    }

    /// <summary>
    /// Lists the argument indexes of the <c>string.Format</c> placeholders in a text: every opening brace
    /// followed at once by a decimal index and closed by a later brace.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <returns>The digits of each placeholder's index, in text order.</returns>
    private static IEnumerable<string> PlaceholderIndexes(string text)
    {
        var open = text.IndexOf('{', StringComparison.Ordinal);

        while (open >= 0)
        {
            var digits = open + 1;
            var end = digits;

            while (end < text.Length && char.IsDigit(text[end]))
            {
                end++;
            }

            var close = end > digits ? text.IndexOf('}', end) : -1;

            if (close >= 0)
            {
                yield return text[digits..end];
            }

            open = text.IndexOf('{', Math.Max(close, open) + 1);
        }
    }
}
