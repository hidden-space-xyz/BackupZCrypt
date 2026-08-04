using System.Globalization;

using BackupZCrypt.Desktop.Converters;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for <see cref="PasswordConverters"/>. The masking character is not cosmetic: Avalonia disables
/// copy and cut while a text box is masked, so clearing it is what allows a password to be copied. An
/// inverted mapping would leave every password field both unmasked and copyable by default.
/// </summary>
public sealed class PasswordConvertersTests
{
    [Test]
    public void RevealToPasswordChar_MasksUnlessTheUserAskedToReveal()
    {
        var converter = PasswordConverters.RevealToPasswordChar;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                converter.Convert(false, typeof(char), null, CultureInfo.InvariantCulture),
                Is.EqualTo('●'),
                "A hidden password must be masked, which is also what keeps copy and cut disabled."
            );
            Assert.That(
                converter.Convert(true, typeof(char), null, CultureInfo.InvariantCulture),
                Is.EqualTo('\0'),
                "Only a deliberately revealed password may clear the mask and so become copyable."
            );
        }
    }

    [TestCase(PasswordStrength.VeryWeak, "danger")]
    [TestCase(PasswordStrength.Weak, "danger")]
    [TestCase(PasswordStrength.Fair, "warning")]
    [TestCase(PasswordStrength.Good, "good")]
    [TestCase(PasswordStrength.Strong, "strong")]
    public void StrengthIsBand_MatchesExactlyOneBandPerStrength(
        PasswordStrength strength,
        string expectedBand
    )
    {
        string[] bands = ["danger", "warning", "good", "strong"];

        var matched = bands
            .Where(band =>
                (bool)
                    PasswordConverters.StrengthIsBand.Convert(
                        strength,
                        typeof(bool),
                        band,
                        CultureInfo.InvariantCulture
                    )!
            )
            .ToList();

        Assert.That(
            matched,
            Is.EqualTo([expectedBand]),
            "Each strength must light exactly one style class: none leaves the bar on the default "
                + "accent colour, and two would let style order decide the colour."
        );
    }

    [Test]
    public void StrengthIsBand_EveryDeclaredStrength_HasABand()
    {
        var unmapped = Enum.GetValues<PasswordStrength>()
            .Where(strength =>
                !new[] { "danger", "warning", "good", "strong" }
                    .Any(band =>
                        (bool)
                            PasswordConverters.StrengthIsBand.Convert(
                                strength,
                                typeof(bool),
                                band,
                                CultureInfo.InvariantCulture
                            )!
                    )
            )
            .ToList();

        Assert.That(
            unmapped,
            Is.Empty,
            "A new PasswordStrength member with no colour band would silently show the default accent."
        );
    }
}
