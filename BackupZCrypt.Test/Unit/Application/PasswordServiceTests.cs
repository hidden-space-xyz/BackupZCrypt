using BackupZCrypt.Application.Services;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the password service's strength analysis and password generation.
/// </summary>
/// <remarks>
/// Non-ASCII test data is written as a Unicode escape rather than as a literal character, so the fixture
/// stays pure ASCII on disk and cannot be broken by a checkout or a build that guesses the source encoding
/// wrong.
/// </remarks>
public sealed class PasswordServiceTests
{
    /// <summary>
    /// The number of characters the composition test asks for. It is large enough that a missing class
    /// means the class is genuinely absent from the pool rather than unlucky: the scarcest class supplies
    /// ten of the eighty-eight symbols, so the odds of it never being drawn are about one in thirty billion.
    /// </summary>
    private const int CompositionSampleLength = 200;

    /// <summary>
    /// The entropy, in bits, that the service treats as a perfect password and scales its score against.
    /// </summary>
    private const double MaxEntropyBits = 120.0;

    /// <summary>
    /// The alphabet each character-class option is expected to contribute to a generated password. Driving
    /// the generation tests from this table means a new option cannot be added without deciding here which
    /// characters it brings, and a change to an existing alphabet has to be made in both places.
    /// </summary>
    private static readonly Dictionary<PasswordGenerationOptions, string> ClassAlphabets = new()
    {
        [PasswordGenerationOptions.IncludeUppercase] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        [PasswordGenerationOptions.IncludeLowercase] = "abcdefghijklmnopqrstuvwxyz",
        [PasswordGenerationOptions.IncludeNumbers] = "0123456789",
        [PasswordGenerationOptions.IncludeSpecialCharacters] = "!@#$%^&*()-_=+[]{}|;:,.<>?",
    };

    /// <summary>
    /// The password service under test; it is stateless, so the fresh instance xUnit constructs for every
    /// test carries no state from the previous one and costs nothing to build.
    /// </summary>
    private readonly PasswordService sut = new();

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    internal void AnalyzePasswordStrength_NullOrEmpty_ReturnsVeryWeakWithNoTips(string? password)
    {
        var analysis = this.sut.AnalyzePasswordStrength(password!);

        Assert.Multiple(
            () => Assert.Equal(PasswordStrength.VeryWeak, analysis.Strength),
            () => Assert.Equal(0d, analysis.Score),
            () => Assert.Equal(0d, analysis.Entropy),
            () => Assert.Empty(analysis.Tips)
        );
    }

    [Fact]
    internal void AnalyzePasswordStrength_ShortAllLowercase_IsWeakAndSuggestsAllMissingCategories()
    {
        var analysis = this.sut.AnalyzePasswordStrength("abc");

        Assert.Multiple(
            () =>
                Assert.True(
                    analysis.Strength is PasswordStrength.VeryWeak or PasswordStrength.Weak,
                    $"Expected a weak strength but got {analysis.Strength}."
                ),
            () => Assert.Contains(MessageCode.TipIncreaseLength, analysis.Tips)
        );
        Assert.Contains(MessageCode.TipAddUppercase, analysis.Tips);
        Assert.Contains(MessageCode.TipAddDigits, analysis.Tips);
        Assert.Contains(MessageCode.TipAddSymbols, analysis.Tips);
    }

    [Fact]
    internal void AnalyzePasswordStrength_LongMixedRandom_IsGoodOrStrongAndScoresHigherThanWeak()
    {
        var weak = this.sut.AnalyzePasswordStrength("abc");
        var strong = this.sut.AnalyzePasswordStrength("Gx7#tQ2!vR9@mZ4&pL6$");

        Assert.Multiple(
            () =>
                Assert.True(
                    strong.Strength is PasswordStrength.Good or PasswordStrength.Strong,
                    $"Expected Good or Strong but got {strong.Strength}."
                ),
            () =>
                Assert.True(
                    strong.Score > weak.Score,
                    $"Expected the strong password score ({strong.Score}) to exceed the weak one ({weak.Score})."
                )
        );
    }

    [Fact]
    internal void AnalyzePasswordStrength_LinearSequence_AddsAvoidSequencesTip()
    {
        var analysis = this.sut.AnalyzePasswordStrength("abcdefgh");

        Assert.Contains(MessageCode.TipAvoidSequences, analysis.Tips);
    }

    [Fact]
    internal void AnalyzePasswordStrength_RepeatedCharacters_AddsReduceRepeatsTip()
    {
        var analysis = this.sut.AnalyzePasswordStrength("aaaaaaaa");

        Assert.Contains(MessageCode.TipReduceRepeats, analysis.Tips);
    }

    [Fact]
    internal void AnalyzePasswordStrength_ContainsYear_AddsAvoidYearsTip()
    {
        var analysis = this.sut.AnalyzePasswordStrength("summer-1999!");

        Assert.Contains(MessageCode.TipAvoidYears, analysis.Tips);
    }

    [Fact]
    internal void GeneratePassword_ReturnsRequestedLength()
    {
        var password = this.sut.GeneratePassword(
            24,
            PasswordGenerationOptions.IncludeUppercase
                | PasswordGenerationOptions.IncludeLowercase
                | PasswordGenerationOptions.IncludeNumbers
                | PasswordGenerationOptions.IncludeSpecialCharacters
        );

        Assert.Equal(24, password.Length);
    }

    [Fact]
    internal void GeneratePassword_UppercaseOnly_ProducesOnlyUppercaseLetters()
    {
        var password = this.sut.GeneratePassword(40, PasswordGenerationOptions.IncludeUppercase);

        Assert.All(password, static c => Assert.InRange(c, 'A', 'Z'));
    }

    [Fact]
    internal void GeneratePassword_NumbersOnly_ProducesOnlyDigits()
    {
        var password = this.sut.GeneratePassword(40, PasswordGenerationOptions.IncludeNumbers);

        Assert.All(password, static c => Assert.InRange(c, '0', '9'));
    }

    [Fact]
    internal void GeneratePassword_ExcludeSimilarCharacters_OmitsAmbiguousCharacters()
    {
        const string Ambiguous = "il1Lo0O";

        var password = this.sut.GeneratePassword(
            200,
            PasswordGenerationOptions.IncludeUppercase
                | PasswordGenerationOptions.IncludeLowercase
                | PasswordGenerationOptions.IncludeNumbers
                | PasswordGenerationOptions.ExcludeSimilarCharacters
        );

        Assert.DoesNotContain(password, c => Ambiguous.Contains(c, StringComparison.Ordinal));
    }

    [Fact]
    internal void GeneratePassword_NoneOption_Throws()
    {
        _ = Assert.Throws<ArgumentException>(
            () => this.sut.GeneratePassword(16, PasswordGenerationOptions.None)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    internal void GeneratePassword_NonPositiveLength_Throws(int length)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => this.sut.GeneratePassword(length, PasswordGenerationOptions.IncludeLowercase)
        );
    }

    [Fact]
    internal void GeneratePassword_SuccessiveCalls_ProduceDifferentResults()
    {
        const PasswordGenerationOptions Options =
            PasswordGenerationOptions.IncludeUppercase
            | PasswordGenerationOptions.IncludeLowercase
            | PasswordGenerationOptions.IncludeNumbers
            | PasswordGenerationOptions.IncludeSpecialCharacters;

        var first = this.sut.GeneratePassword(32, Options);
        var second = this.sut.GeneratePassword(32, Options);

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("kfm7xbv2z", 36, 10.0, PasswordStrength.Weak)]
    [InlineData("k7fm2xb9vzq", 36, 0.0, PasswordStrength.Fair)]
    [InlineData("Tk9#vQ2!wR5@m", 94, 0.0, PasswordStrength.Good)]
    internal void AnalyzePasswordStrength_PatternFreePassword_ScoresPoolEntropyLessTheHomogeneityPenalty(
        string password,
        int expectedPoolSize,
        double expectedPenalty,
        PasswordStrength expectedStrength
    )
    {
        var analysis = this.sut.AnalyzePasswordStrength(password);

        var expectedEntropy = (password.Length * Math.Log2(expectedPoolSize)) - expectedPenalty;
        var expectedScore = Math.Round(
            expectedEntropy / MaxEntropyBits * 100.0,
            2,
            MidpointRounding.ToEven
        );

        Assert.Multiple(
            () => Assert.Equal(expectedEntropy, analysis.Entropy, 1e-9),
            () => Assert.Equal(expectedScore, analysis.Score, 1e-9),
            () => Assert.Equal(expectedStrength, analysis.Strength)
        );
    }

    [Fact]
    internal void AnalyzePasswordStrength_SixteenCharactersOfEveryClass_AddsTheAllRoundBonusOnTopOfTheEntropyScore()
    {
        const string Password = "Tk9#vQ2!wR5@mZ7$";

        var analysis = this.sut.AnalyzePasswordStrength(Password);

        var expectedEntropy = Password.Length * Math.Log2(94);
        var scoreWithoutBonus = expectedEntropy / MaxEntropyBits * 100.0;

        Assert.Multiple(
            () => Assert.Equal(expectedEntropy, analysis.Entropy, 1e-9),
            () =>
                Assert.Equal(
                    Math.Round(scoreWithoutBonus + 5.0, 2, MidpointRounding.ToEven),
                    analysis.Score,
                    1e-9
                ),
            () => Assert.Equal(PasswordStrength.Strong, analysis.Strength)
        );
    }

    [Fact]
    internal void AnalyzePasswordStrength_NonAsciiCharacter_CreditsTheUnicodePoolInsteadOfIgnoringIt()
    {
        const string Ascii = "kfmxbvzqh";

        const string Accented = "kfmxbvzq\u00E9";

        var asciiAnalysis = this.sut.AnalyzePasswordStrength(Ascii);
        var accentedAnalysis = this.sut.AnalyzePasswordStrength(Accented);

        var expectedEntropy = (Accented.Length * Math.Log2(26 + 50)) - 10.0;

        Assert.Multiple(
            () => Assert.Equal(expectedEntropy, accentedAnalysis.Entropy, 1e-9),
            () =>
                Assert.True(
                    accentedAnalysis.Entropy > asciiAnalysis.Entropy,
                    "replacing one ASCII letter with a non-ASCII one must never lower the estimated entropy"
                )
        );
    }

    [Fact]
    internal void AnalyzePasswordStrength_OnlyUnrecognizedCharacters_ScoresZeroAndAsksForEveryClass()
    {
        var analysis = this.sut.AnalyzePasswordStrength("~~~~~~~~");

        Assert.Multiple(
            () => Assert.Equal(0d, analysis.Entropy),
            () => Assert.Equal(0d, analysis.Score),
            () => Assert.Equal(PasswordStrength.VeryWeak, analysis.Strength),
            () => Assert.Contains(MessageCode.TipAddLowercase, analysis.Tips),
            () => Assert.Contains(MessageCode.TipAddUppercase, analysis.Tips)
        );
    }

    [Fact]
    internal void GeneratePassword_EveryClassRequested_DrawsFromEachOfThemAndFromNothingElse()
    {
        var options = ClassAlphabets.Keys.Aggregate(
            PasswordGenerationOptions.None,
            static (combined, option) => combined | option
        );
        var unionAlphabet = string.Concat(ClassAlphabets.Values);

        var password = this.sut.GeneratePassword(CompositionSampleLength, options);

        Assert.Multiple(
            () => Assert.Equal(CompositionSampleLength, password.Length),
            () =>
                Assert.DoesNotContain(
                    password,
                    c => !unionAlphabet.Contains(c, StringComparison.Ordinal)
                ),
            () =>
                Assert.All(
                    ClassAlphabets.Values,
                    alphabet =>
                        Assert.Contains(password, c => alphabet.Contains(c, StringComparison.Ordinal))
                )
        );
    }

    [Fact]
    internal void GeneratePassword_OnlyTheExclusionOptionRequested_ThrowsInsteadOfReturningAnEmptyPassword()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => this.sut.GeneratePassword(16, PasswordGenerationOptions.ExcludeSimilarCharacters)
        );

        Assert.Multiple(
            () => Assert.Equal("options", exception.ParamName),
            () =>
                Assert.Contains(
                    "No characters available",
                    exception.Message,
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    internal void GeneratePassword_EveryGenerationOption_IsAccountedForByTheAlphabetTable()
    {
        var accountedFor = ClassAlphabets
            .Keys.Append(PasswordGenerationOptions.None)
            .Append(PasswordGenerationOptions.ExcludeSimilarCharacters);

        Assert.Equivalent(accountedFor, Enum.GetValues<PasswordGenerationOptions>(), strict: true);
    }
}
