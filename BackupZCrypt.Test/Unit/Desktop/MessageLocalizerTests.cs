using System.Globalization;
using System.Text.RegularExpressions;

using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for <see cref="MessageLocalizer"/>, the single point where the language-neutral codes the
/// lower layers emit become text the user reads. Every case is driven from <c>Enum.GetValues</c> and from
/// the placeholders the resources themselves declare, so a code added without a resource — which the app
/// would silently render as the raw enum name — fails here rather than in front of a user.
/// </summary>
public sealed partial class MessageLocalizerTests
{
    /// <summary>
    /// The suffix that, by convention, marks a code whose resource consumes <c>string.Format</c> arguments.
    /// </summary>
    private const string FormatSuffix = "Format";

    /// <summary>
    /// The match timeout the placeholder pattern runs under, mirroring the bound the production
    /// patterns carry so no regex in the solution is left unbounded.
    /// </summary>
    private const int RegexTimeoutMilliseconds = 1000;

    /// <summary>
    /// Gets the pattern matching the opening of a <c>string.Format</c> placeholder, capturing its
    /// argument index.
    /// </summary>
    /// <value>The placeholder pattern.</value>
    [GeneratedRegex(@"\{(?<index>\d+)", RegexOptions.ExplicitCapture, RegexTimeoutMilliseconds)]
    private static partial Regex PlaceholderPattern { get; }

    [Test]
    public void Localize_EveryMessageCode_ResolvesToLocalizedTextRatherThanTheCodeName()
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

        Assert.That(
            offenders,
            Is.Empty,
            "MessageCode members that resolved to nothing or to their own name, which is what the UI would then show: "
                + string.Join(", ", offenders)
        );
    }

    [Test]
    public void Localize_EveryCodeNamedFormat_SubstitutesEveryArgumentItWasGiven()
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
            else if (PlaceholderPattern.IsMatch(text))
            {
                offenders.Add($"{name} (left an unsubstituted placeholder in '{text}')");
            }
        }

        Assert.That(
            offenders,
            Is.Empty,
            "Format-suffixed MessageCode members whose arguments were not consumed as the convention promises: "
                + string.Join("; ", offenders)
        );
    }

    [Test]
    public void Localize_EveryCodeNotNamedFormat_RendersWithoutLeavingAPlaceholder()
    {
        var offenders = Enum.GetValues<MessageCode>()
            .Where(static code =>
                !code.ToString().EndsWith(FormatSuffix, StringComparison.Ordinal)
                && PlaceholderPattern.IsMatch(MessageLocalizer.Localize(new LocalizableMessage(code)))
            )
            .Select(static code => code.ToString())
            .ToList();

        Assert.That(
            offenders,
            Is.Empty,
            "MessageCode members whose resource contains a placeholder but whose name does not end in "
                + $"'{FormatSuffix}', so no arguments are ever applied and the UI shows a literal '{{0}}': "
                + string.Join(", ", offenders)
        );
    }

    [Test]
    public void Localize_CodeWithNoMatchingResource_FallsBackToTheCodeItselfInsteadOfThrowing()
    {
        var unmapped = (MessageCode)(-1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                MessageLocalizer.Localize(new LocalizableMessage(unmapped)),
                Is.EqualTo("-1"),
                "An unmapped code must degrade to its own identifier rather than to an empty caption."
            );
            Assert.That(
                MessageLocalizer.Localize(new LocalizableMessage(unmapped, "detail")),
                Is.EqualTo("-1"),
                "An unmapped code carrying arguments must not throw either: this runs while the app is already reporting a failure."
            );
        }
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
        var highestIndex = PlaceholderPattern
            .Matches(Strings.GetByKey(key))
            .Select(static match =>
                int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture)
            )
            .DefaultIfEmpty(-1)
            .Max();

        return
        [
            .. Enumerable.Range(0, highestIndex + 1)
                .Select(static index => (object)string.Create(CultureInfo.InvariantCulture, $"ARG{index}")),
        ];
    }
}
