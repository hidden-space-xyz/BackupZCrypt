using System.Globalization;

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
    /// The password service under test; it is stateless, so one instance is shared by every test.
    /// </summary>
    private readonly PasswordService sut = new();

    [TestCase(null)]
    [TestCase("")]
    public void AnalyzePasswordStrength_NullOrEmpty_ReturnsVeryWeakWithNoTips(string? password)
    {
        var analysis = this.sut.AnalyzePasswordStrength(password!);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(analysis.Strength, Is.EqualTo(PasswordStrength.VeryWeak));
            Assert.That(analysis.Score, Is.Zero);
            Assert.That(analysis.Entropy, Is.Zero);
            Assert.That(analysis.Tips, Is.Empty);
        }
    }

    [Test]
    public void AnalyzePasswordStrength_ShortAllLowercase_IsWeakAndSuggestsAllMissingCategories()
    {
        var analysis = this.sut.AnalyzePasswordStrength("abc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                    analysis.Strength,
                    Is.EqualTo(PasswordStrength.VeryWeak).Or.EqualTo(PasswordStrength.Weak),
                    $"Expected a weak strength but got {analysis.Strength}."
                );
            Assert.That(analysis.Tips, Does.Contain(MessageCode.TipIncreaseLength));
        }
        Assert.That(analysis.Tips, Does.Contain(MessageCode.TipAddUppercase));
        Assert.That(analysis.Tips, Does.Contain(MessageCode.TipAddDigits));
        Assert.That(analysis.Tips, Does.Contain(MessageCode.TipAddSymbols));
    }

    [Test]
    public void AnalyzePasswordStrength_LongMixedRandom_IsGoodOrStrongAndScoresHigherThanWeak()
    {
        var weak = this.sut.AnalyzePasswordStrength("abc");
        var strong = this.sut.AnalyzePasswordStrength("Gx7#tQ2!vR9@mZ4&pL6$");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                    strong.Strength,
                    Is.EqualTo(PasswordStrength.Good).Or.EqualTo(PasswordStrength.Strong),
                    $"Expected Good or Strong but got {strong.Strength}."
                );
            Assert.That(
                strong.Score,
                Is.GreaterThan(weak.Score),
                $"Expected the strong password score ({strong.Score}) to exceed the weak one ({weak.Score})."
            );
        }
    }

    [Test]
    public void AnalyzePasswordStrength_LinearSequence_AddsAvoidSequencesTip()
    {
        var analysis = this.sut.AnalyzePasswordStrength("abcdefgh");

        Assert.That(analysis.Tips, Does.Contain(MessageCode.TipAvoidSequences));
    }

    [Test]
    public void AnalyzePasswordStrength_RepeatedCharacters_AddsReduceRepeatsTip()
    {
        var analysis = this.sut.AnalyzePasswordStrength("aaaaaaaa");

        Assert.That(analysis.Tips, Does.Contain(MessageCode.TipReduceRepeats));
    }

    [Test]
    public void AnalyzePasswordStrength_ContainsYear_AddsAvoidYearsTip()
    {
        var analysis = this.sut.AnalyzePasswordStrength("summer-1999!");

        Assert.That(analysis.Tips, Does.Contain(MessageCode.TipAvoidYears));
    }

    [Test]
    public void GeneratePassword_ReturnsRequestedLength()
    {
        var password = this.sut.GeneratePassword(
            24,
            PasswordGenerationOptions.IncludeUppercase
                | PasswordGenerationOptions.IncludeLowercase
                | PasswordGenerationOptions.IncludeNumbers
                | PasswordGenerationOptions.IncludeSpecialCharacters
        );

        Assert.That(password, Has.Length.EqualTo(24));
    }

    [Test]
    public void GeneratePassword_UppercaseOnly_ProducesOnlyUppercaseLetters()
    {
        var password = this.sut.GeneratePassword(40, PasswordGenerationOptions.IncludeUppercase);

        Assert.That(password, Has.All.InRange('A', 'Z'));
    }

    [Test]
    public void GeneratePassword_NumbersOnly_ProducesOnlyDigits()
    {
        var password = this.sut.GeneratePassword(40, PasswordGenerationOptions.IncludeNumbers);

        Assert.That(password, Has.All.InRange('0', '9'));
    }

    [Test]
    public void GeneratePassword_ExcludeSimilarCharacters_OmitsAmbiguousCharacters()
    {
        const string Ambiguous = "il1Lo0O";

        var password = this.sut.GeneratePassword(
            200,
            PasswordGenerationOptions.IncludeUppercase
                | PasswordGenerationOptions.IncludeLowercase
                | PasswordGenerationOptions.IncludeNumbers
                | PasswordGenerationOptions.ExcludeSimilarCharacters
        );

        Assert.That(
            password,
            Has.None.Matches<char>(c => Ambiguous.Contains(c, StringComparison.Ordinal))
        );
    }

    [Test]
    public void GeneratePassword_NoneOption_Throws()
    {
        _ = Assert.Throws<ArgumentException>(
            () => this.sut.GeneratePassword(16, PasswordGenerationOptions.None)
        );
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void GeneratePassword_NonPositiveLength_Throws(int length)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => this.sut.GeneratePassword(length, PasswordGenerationOptions.IncludeLowercase)
        );
    }

    [Test]
    public void GeneratePassword_SuccessiveCalls_ProduceDifferentResults()
    {
        const PasswordGenerationOptions Options =
            PasswordGenerationOptions.IncludeUppercase
            | PasswordGenerationOptions.IncludeLowercase
            | PasswordGenerationOptions.IncludeNumbers
            | PasswordGenerationOptions.IncludeSpecialCharacters;

        var first = this.sut.GeneratePassword(32, Options);
        var second = this.sut.GeneratePassword(32, Options);

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [TestCase("kfm7xbv2z", 36, 10.0, PasswordStrength.Weak)]
    [TestCase("k7fm2xb9vzq", 36, 0.0, PasswordStrength.Fair)]
    [TestCase("Tk9#vQ2!wR5@m", 94, 0.0, PasswordStrength.Good)]
    public void AnalyzePasswordStrength_PatternFreePassword_ScoresPoolEntropyLessTheHomogeneityPenalty(
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                analysis.Entropy,
                Is.EqualTo(expectedEntropy).Within(1e-9),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{password}' contains no repeat, sequence, common word, or year, so its entropy must be exactly "
                        + $"{password.Length} characters drawn from a {expectedPoolSize}-symbol alphabet, less the "
                        + $"{expectedPenalty}-bit penalty charged for spanning too few character classes"
                )
            );
            Assert.That(analysis.Score, Is.EqualTo(expectedScore).Within(1e-9));
            Assert.That(
                analysis.Strength,
                Is.EqualTo(expectedStrength),
                $"a score of {analysis.Score} was rated {analysis.Strength}, which moves the advertised strength bands"
            );
        }
    }

    [Test]
    public void AnalyzePasswordStrength_SixteenCharactersOfEveryClass_AddsTheAllRoundBonusOnTopOfTheEntropyScore()
    {
        const string Password = "Tk9#vQ2!wR5@mZ7$";

        var analysis = this.sut.AnalyzePasswordStrength(Password);

        var expectedEntropy = Password.Length * Math.Log2(94);
        var scoreWithoutBonus = expectedEntropy / MaxEntropyBits * 100.0;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(analysis.Entropy, Is.EqualTo(expectedEntropy).Within(1e-9));
            Assert.That(
                analysis.Score,
                Is.EqualTo(Math.Round(scoreWithoutBonus + 5.0, 2, MidpointRounding.ToEven)).Within(1e-9),
                "a 16-character password that uses all four classes and clears 90 bits must earn the 5-point "
                    + "bonus for being strong on every axis at once"
            );
            Assert.That(analysis.Strength, Is.EqualTo(PasswordStrength.Strong));
        }
    }

    [Test]
    public void AnalyzePasswordStrength_NonAsciiCharacter_CreditsTheUnicodePoolInsteadOfIgnoringIt()
    {
        const string Ascii = "kfmxbvzqh";

        const string Accented = "kfmxbvzq\u00E9";

        var asciiAnalysis = this.sut.AnalyzePasswordStrength(Ascii);
        var accentedAnalysis = this.sut.AnalyzePasswordStrength(Accented);

        var expectedEntropy = (Accented.Length * Math.Log2(26 + 50)) - 10.0;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                accentedAnalysis.Entropy,
                Is.EqualTo(expectedEntropy).Within(1e-9),
                "a non-ASCII character must widen the assumed search alphabet by the conservative 50-symbol Unicode pool"
            );
            Assert.That(
                accentedAnalysis.Entropy,
                Is.GreaterThan(asciiAnalysis.Entropy),
                "replacing one ASCII letter with a non-ASCII one must never lower the estimated entropy"
            );
        }
    }

    [Test]
    public void AnalyzePasswordStrength_OnlyUnrecognizedCharacters_ScoresZeroAndAsksForEveryClass()
    {
        var analysis = this.sut.AnalyzePasswordStrength("~~~~~~~~");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                analysis.Entropy,
                Is.Zero,
                "characters that belong to no recognized class must be credited with no entropy at all"
            );
            Assert.That(analysis.Score, Is.Zero);
            Assert.That(analysis.Strength, Is.EqualTo(PasswordStrength.VeryWeak));
            Assert.That(analysis.Tips, Does.Contain(MessageCode.TipAddLowercase));
            Assert.That(analysis.Tips, Does.Contain(MessageCode.TipAddUppercase));
        }
    }

    [Test]
    public void GeneratePassword_EveryClassRequested_DrawsFromEachOfThemAndFromNothingElse()
    {
        var options = ClassAlphabets.Keys.Aggregate(
            PasswordGenerationOptions.None,
            static (combined, option) => combined | option
        );
        var unionAlphabet = string.Concat(ClassAlphabets.Values);

        var password = this.sut.GeneratePassword(CompositionSampleLength, options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(password, Has.Length.EqualTo(CompositionSampleLength));
            Assert.That(
                password,
                Has.None.Matches<char>(c => !unionAlphabet.Contains(c, StringComparison.Ordinal)),
                "the generator drew a character that belongs to none of the requested classes"
            );

            foreach (var (option, alphabet) in ClassAlphabets)
            {
                Assert.That(
                    password,
                    Has.Some.Matches<char>(c => alphabet.Contains(c, StringComparison.Ordinal)),
                    $"{CompositionSampleLength} characters contained nothing from {option}, so that class is missing from the pool"
                );
            }
        }
    }

    [Test]
    public void GeneratePassword_OnlyTheExclusionOptionRequested_ThrowsInsteadOfReturningAnEmptyPassword()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => this.sut.GeneratePassword(16, PasswordGenerationOptions.ExcludeSimilarCharacters)
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("options"));
            Assert.That(
                exception.Message,
                Does.Contain("No characters available"),
                "excluding ambiguous characters without selecting a single class leaves an empty pool, which must be "
                    + "reported as such rather than silently accepted"
            );
        }
    }

    [Test]
    public void GeneratePassword_EveryGenerationOption_IsAccountedForByTheAlphabetTable()
    {
        var accountedFor = ClassAlphabets
            .Keys.Append(PasswordGenerationOptions.None)
            .Append(PasswordGenerationOptions.ExcludeSimilarCharacters);

        Assert.That(
            Enum.GetValues<PasswordGenerationOptions>(),
            Is.EquivalentTo(accountedFor),
            "a password generation option was added without declaring which characters it contributes, so the "
                + "generator can silently ignore it"
        );
    }
}
