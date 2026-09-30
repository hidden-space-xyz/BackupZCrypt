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

    /// <summary>
    /// The level of every strength meter segment, as the XAML passes it to the converter.
    /// </summary>
    private static readonly string[] MeterLevels = ["1", "2", "3", "4", "5"];

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

    [Theory]
    [InlineData(PasswordStrength.VeryWeak, 1)]
    [InlineData(PasswordStrength.Weak, 2)]
    [InlineData(PasswordStrength.Fair, 3)]
    [InlineData(PasswordStrength.Good, 4)]
    [InlineData(PasswordStrength.Strong, 5)]
    internal void StrengthReaches_LightsOneMoreMeterSegmentForEachStrongerStrength(
        PasswordStrength strength,
        int expectedLitSegments
    )
    {
        var lit = MeterLevels.Count(level => Reaches(strength, level));

        Assert.Equal(expectedLitSegments, lit);
    }

    [Fact]
    internal void StrengthReaches_ForAStrengthOutsideTheEnum_LightsOnlyTheFirstSegment()
    {
        var lit = MeterLevels.Where(level => Reaches((PasswordStrength)99, level)).ToList();

        Assert.Equal<string>(["1"], lit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("first")]
    internal void StrengthReaches_WithoutANumericLevel_LightsNothing(string? level)
    {
        Assert.False(Reaches(PasswordStrength.Strong, level));
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

    /// <summary>
    /// Asks the meter converter whether a strength lights the segment at the given level.
    /// </summary>
    /// <param name="strength">The strength being shown.</param>
    /// <param name="level">The segment level, as the XAML passes it.</param>
    /// <returns><see langword="true"/> if the segment is lit; otherwise <see langword="false"/>.</returns>
    private static bool Reaches(PasswordStrength strength, string? level)
    {
        return (bool)
            PasswordConverters.StrengthReaches.Convert(
                strength,
                typeof(bool),
                level,
                CultureInfo.InvariantCulture
            )!;
    }
}
