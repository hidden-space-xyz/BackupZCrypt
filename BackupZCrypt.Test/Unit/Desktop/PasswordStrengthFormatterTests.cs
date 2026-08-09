using System.Globalization;

using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for <see cref="PasswordStrengthFormatter"/>. The caption it builds is the only feedback a user
/// gets about the password that protects an unrecoverable backup, so the tests pin which label each strength
/// carries, that an unrecognized strength degrades to the weakest label rather than the strongest, and that
/// the suggestion list and the congratulation stay mutually exclusive.
/// </summary>
public sealed class PasswordStrengthFormatterTests
{
    /// <summary>
    /// The label every strength must carry, kept as an independent map so that a mis-wired arm in the
    /// formatter's switch cannot be masked by the same mistake being repeated in the test.
    /// </summary>
    private static readonly Dictionary<PasswordStrength, string> ExpectedLabels = new()
    {
        [PasswordStrength.VeryWeak] = Strings.StrengthVeryWeak,
        [PasswordStrength.Weak] = Strings.StrengthWeak,
        [PasswordStrength.Fair] = Strings.StrengthFair,
        [PasswordStrength.Good] = Strings.StrengthGood,
        [PasswordStrength.Strong] = Strings.StrengthStrong,
    };

    /// <summary>
    /// Tips in the order the formatter receives them; no two of them are substrings of one another in either
    /// shipped language, so "contains" assertions over them are unambiguous.
    /// </summary>
    private static readonly MessageCode[] SampleTips =
    [
        MessageCode.TipIncreaseLength,
        MessageCode.TipAddUppercase,
        MessageCode.TipAddDigits,
        MessageCode.TipAddSymbols,
        MessageCode.TipAvoidYears,
    ];

    /// <summary>
    /// Gets every defined strength, so that adding a member to the enum adds a case here automatically.
    /// </summary>
    public static TheoryData<PasswordStrength> DefinedStrengths => new(Enum.GetValues<PasswordStrength>());

    [Theory]
    [MemberData(nameof(DefinedStrengths))]
    internal void Format_ForEachDefinedStrength_LeadsWithThatStrengthsOwnLabel(PasswordStrength strength)
    {
        _ = Assert.Contains(strength, ExpectedLabels);

        var expected = ExpectedLabels[strength];
        var caption = PasswordStrengthFormatter.Format(new PasswordStrengthAnalysis(strength, 50, 64.0, []));

        Assert.StartsWith(expected, caption, StringComparison.Ordinal);
    }

    [Fact]
    internal void Format_StrengthOutsideTheEnum_FallsBackToTheWeakestLabel()
    {
        var caption = PasswordStrengthFormatter.Format(new PasswordStrengthAnalysis((PasswordStrength)99, 0, 0, []));

        Assert.StartsWith(Strings.StrengthVeryWeak, caption, StringComparison.Ordinal);
    }

    [Fact]
    internal void Format_WithMoreTipsThanTheCap_KeepsOnlyTheFirstThree()
    {
        var caption = PasswordStrengthFormatter.Format(
            new PasswordStrengthAnalysis(PasswordStrength.Weak, 20, 30.0, SampleTips)
        );

        Assert.Multiple(
            () => Assert.Contains(Localize(SampleTips[0]), caption, StringComparison.Ordinal),
            () => Assert.Contains(Localize(SampleTips[1]), caption, StringComparison.Ordinal),
            () => Assert.Contains(Localize(SampleTips[2]), caption, StringComparison.Ordinal),
            () => Assert.DoesNotContain(Localize(SampleTips[3]), caption, StringComparison.Ordinal),
            () => Assert.DoesNotContain(Localize(SampleTips[4]), caption, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(PasswordStrength.Strong, 0, false, true)]
    [InlineData(PasswordStrength.Strong, 1, true, false)]
    [InlineData(PasswordStrength.Weak, 0, false, false)]
    [InlineData(PasswordStrength.Weak, 2, true, false)]
    internal void Format_SuggestionsAndCongratulation_AreGatedOnTipsFirstAndStrengthSecond(
        PasswordStrength strength,
        int tipCount,
        bool expectsSuggestions,
        bool expectsCongratulation
    )
    {
        var analysis = new PasswordStrengthAnalysis(strength, 50, 64.0, [.. SampleTips.Take(tipCount)]);

        var caption = PasswordStrengthFormatter.Format(analysis);

        Assert.Multiple(
            () => Assert.Equal(
                expectsSuggestions,
                caption.Contains(Strings.Suggestions, StringComparison.Ordinal)
            ),
            () => Assert.Equal(
                expectsCongratulation,
                caption.Contains(Strings.GoodJob, StringComparison.Ordinal)
            )
        );
    }

    [Fact]
    internal void Format_UnderACommaDecimalCulture_RendersEntropyWithThatCulturesSeparator()
    {
        var commaCulture = TryGetCommaDecimalCulture();
        if (commaCulture is null)
        {
            Assert.Skip("This runtime exposes no comma-decimal culture, so ambient formatting cannot differ.");
        }

        var previousCulture = CultureInfo.CurrentCulture;
        string caption;
        try
        {
            CultureInfo.CurrentCulture = commaCulture;
            caption = PasswordStrengthFormatter.Format(
                new PasswordStrengthAnalysis(PasswordStrength.Good, 70, 72.53, [])
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        Assert.Multiple(
            () => Assert.Contains("72,5", caption, StringComparison.Ordinal),
            () => Assert.DoesNotContain("72.5", caption, StringComparison.Ordinal)
        );
    }

    /// <summary>
    /// Finds an installed culture that writes decimals with a comma, so the test can prove the caption
    /// follows the ambient culture without depending on one specific locale being present.
    /// </summary>
    /// <remarks>
    /// The culture is resolved here at run time rather than pinned on the test with <c>[SetCulture("es-ES")]</c>:
    /// that attribute is applied before the test body runs and throws <see cref="CultureNotFoundException"/> on a
    /// runtime built in globalization-invariant mode (a slim container without ICU), where this scenario cannot
    /// arise at all. GitHub Actions' ubuntu images ship ICU, so the case does execute on CI.
    /// </remarks>
    /// <returns>A comma-decimal culture, or <see langword="null"/> when the runtime exposes none.</returns>
    private static CultureInfo? TryGetCommaDecimalCulture()
    {
        try
        {
            var candidate = CultureInfo.GetCultureInfo("es-ES");
            return string.Equals(candidate.NumberFormat.NumberDecimalSeparator, ",", StringComparison.Ordinal)
                ? candidate
                : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Localizes a bare message code the same way the formatter localizes the tips the Application
    /// layer hands it. The caption's own vocabulary is read from <see cref="Strings"/> directly,
    /// because the formatter picks it here rather than receiving it as a code.
    /// </summary>
    /// <param name="code">The code to localize.</param>
    /// <returns>The localized text for the current UI culture.</returns>
    private static string Localize(MessageCode code)
    {
        return MessageLocalizer.Localize(new LocalizableMessage(code));
    }
}
