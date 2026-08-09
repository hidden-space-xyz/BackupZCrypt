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
    /// <summary>
    /// Every colour band a strength can light, mirroring the style classes the XAML declares.
    /// </summary>
    private static readonly string[] Bands = ["danger", "warning", "good", "strong"];

    [Fact]
    internal void RevealToPasswordChar_MasksUnlessTheUserAskedToReveal()
    {
        var converter = PasswordConverters.RevealToPasswordChar;

        Assert.Multiple(
            () =>
                Assert.Equal(
                    '●',
                    converter.Convert(false, typeof(char), null, CultureInfo.InvariantCulture)
                ),
            () =>
                Assert.Equal(
                    '\0',
                    converter.Convert(true, typeof(char), null, CultureInfo.InvariantCulture)
                )
        );
    }

    [Theory]
    [InlineData(PasswordStrength.VeryWeak, "danger")]
    [InlineData(PasswordStrength.Weak, "danger")]
    [InlineData(PasswordStrength.Fair, "warning")]
    [InlineData(PasswordStrength.Good, "good")]
    [InlineData(PasswordStrength.Strong, "strong")]
    internal void StrengthIsBand_MatchesExactlyOneBandPerStrength(
        PasswordStrength strength,
        string expectedBand
    )
    {
        var matched = Bands
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

        string[] expected = [expectedBand];

        Assert.Equal(expected, matched);
    }

    [Fact]
    internal void StrengthIsBand_EveryDeclaredStrength_HasABand()
    {
        var unmapped = Enum.GetValues<PasswordStrength>()
            .Where(strength =>
                !Bands
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

        Assert.Empty(unmapped);
    }
}
