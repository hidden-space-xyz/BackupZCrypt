using BackupZCrypt.Application.Services;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the password service's strength analysis and password generation.
/// </summary>
public sealed class PasswordServiceTests
{
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
        const string ambiguous = "il1Lo0O";

        var password = this.sut.GeneratePassword(
            200,
            PasswordGenerationOptions.IncludeUppercase
                | PasswordGenerationOptions.IncludeLowercase
                | PasswordGenerationOptions.IncludeNumbers
                | PasswordGenerationOptions.ExcludeSimilarCharacters
        );

        Assert.That(
            password,
            Has.None.Matches<char>(c => ambiguous.Contains(c, StringComparison.Ordinal))
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
        const PasswordGenerationOptions options =
            PasswordGenerationOptions.IncludeUppercase
            | PasswordGenerationOptions.IncludeLowercase
            | PasswordGenerationOptions.IncludeNumbers
            | PasswordGenerationOptions.IncludeSpecialCharacters;

        var first = this.sut.GeneratePassword(32, options);
        var second = this.sut.GeneratePassword(32, options);

        Assert.That(second, Is.Not.EqualTo(first));
    }
}
