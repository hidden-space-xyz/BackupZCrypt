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
    public static IEnumerable<PasswordStrength> DefinedStrengths => Enum.GetValues<PasswordStrength>();

    [TestCaseSource(nameof(DefinedStrengths))]
    public void Format_ForEachDefinedStrength_LeadsWithThatStrengthsOwnLabel(PasswordStrength strength)
    {
        Assert.That(
            ExpectedLabels,
            Does.ContainKey(strength),
            $"PasswordStrength.{strength} has no expected label here; add it alongside the formatter's switch arm."
        );

        var expected = ExpectedLabels[strength];
        var caption = PasswordStrengthFormatter.Format(new PasswordStrengthAnalysis(strength, 50, 64.0, []));

        Assert.That(
            caption,
            Does.StartWith(expected),
            $"PasswordStrength.{strength} must be captioned '{expected}' but the caption was '{caption}'."
        );
    }

    [Test]
    public void Format_StrengthOutsideTheEnum_FallsBackToTheWeakestLabel()
    {
        var caption = PasswordStrengthFormatter.Format(new PasswordStrengthAnalysis((PasswordStrength)99, 0, 0, []));

        Assert.That(
            caption,
            Does.StartWith(Strings.StrengthVeryWeak),
            "An unrecognized strength must degrade to the weakest label; degrading upwards would tell a user a bad password is safe."
        );
    }

    [Test]
    public void Format_WithMoreTipsThanTheCap_KeepsOnlyTheFirstThree()
    {
        var caption = PasswordStrengthFormatter.Format(
            new PasswordStrengthAnalysis(PasswordStrength.Weak, 20, 30.0, SampleTips)
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(caption, Does.Contain(Localize(SampleTips[0])), "The first tip must survive the cap.");
            Assert.That(caption, Does.Contain(Localize(SampleTips[1])), "The second tip must survive the cap.");
            Assert.That(caption, Does.Contain(Localize(SampleTips[2])), "The third tip must survive the cap.");
            Assert.That(caption, Does.Not.Contain(Localize(SampleTips[3])), "The fourth tip must be dropped by the cap.");
            Assert.That(caption, Does.Not.Contain(Localize(SampleTips[4])), "The fifth tip must be dropped by the cap.");
        }
    }

    [TestCase(PasswordStrength.Strong, 0, false, true)]
    [TestCase(PasswordStrength.Strong, 1, true, false)]
    [TestCase(PasswordStrength.Weak, 0, false, false)]
    [TestCase(PasswordStrength.Weak, 2, true, false)]
    public void Format_SuggestionsAndCongratulation_AreGatedOnTipsFirstAndStrengthSecond(
        PasswordStrength strength,
        int tipCount,
        bool expectsSuggestions,
        bool expectsCongratulation
    )
    {
        var analysis = new PasswordStrengthAnalysis(strength, 50, 64.0, [.. SampleTips.Take(tipCount)]);

        var caption = PasswordStrengthFormatter.Format(analysis);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                caption.Contains(Strings.Suggestions, StringComparison.Ordinal),
                Is.EqualTo(expectsSuggestions),
                $"Suggestions must appear only when there are tips. Caption: '{caption}'."
            );
            Assert.That(
                caption.Contains(Strings.GoodJob, StringComparison.Ordinal),
                Is.EqualTo(expectsCongratulation),
                $"The congratulation must appear only for a strong password with nothing left to improve. Caption: '{caption}'."
            );
        }
    }

    [Test]
    public void Format_UnderACommaDecimalCulture_RendersEntropyWithThatCulturesSeparator()
    {
        var commaCulture = TryGetCommaDecimalCulture();
        if (commaCulture is null)
        {
            Assert.Ignore("This runtime exposes no comma-decimal culture, so ambient formatting cannot differ.");
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                caption,
                Does.Contain("72,5"),
                $"Entropy must follow the ambient culture like the rest of the caption. Caption: '{caption}'."
            );
            Assert.That(caption, Does.Not.Contain("72.5"), "A hard-coded invariant culture would desync entropy from the caption.");
        }
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
