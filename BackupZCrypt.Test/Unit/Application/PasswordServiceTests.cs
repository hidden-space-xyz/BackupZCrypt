using BackupZCrypt.Application.Services;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

public sealed class PasswordServiceTests
{
    private readonly PasswordService sut = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnalyzePasswordStrength_NullOrEmpty_ReturnsVeryWeakWithNoTips(string? password)
    {
        var analysis = this.sut.AnalyzePasswordStrength(password!);

        Assert.Equal(PasswordStrength.VeryWeak, analysis.Strength);
        Assert.Equal(0, analysis.Score);
        Assert.Equal(0, analysis.Entropy);
        Assert.Empty(analysis.Tips);
    }

    [Fact]
    public void AnalyzePasswordStrength_ShortAllLowercase_IsWeakAndSuggestsAllMissingCategories()
    {
        var analysis = this.sut.AnalyzePasswordStrength("abc");

        // "abc" is short, lowercase-only, and a known common substring/sequence.
        Assert.True(
            analysis.Strength is PasswordStrength.VeryWeak or PasswordStrength.Weak,
            $"Expected a weak strength but got {analysis.Strength}."
        );
        Assert.Contains(MessageCode.TipIncreaseLength, analysis.Tips);
        Assert.Contains(MessageCode.TipAddUppercase, analysis.Tips);
        Assert.Contains(MessageCode.TipAddDigits, analysis.Tips);
        Assert.Contains(MessageCode.TipAddSymbols, analysis.Tips);
    }

    [Fact]
    public void AnalyzePasswordStrength_LongMixedRandom_IsGoodOrStrongAndScoresHigherThanWeak()
    {
        var weak = this.sut.AnalyzePasswordStrength("abc");
        var strong = this.sut.AnalyzePasswordStrength("Gx7#tQ2!vR9@mZ4&pL6$");

        Assert.True(
            strong.Strength is PasswordStrength.Good or PasswordStrength.Strong,
            $"Expected Good or Strong but got {strong.Strength}."
        );
        Assert.True(
            strong.Score > weak.Score,
            $"Expected the strong password score ({strong.Score}) to exceed the weak one ({weak.Score})."
        );
    }

    [Fact]
    public void AnalyzePasswordStrength_LinearSequence_AddsAvoidSequencesTip()
    {
        var analysis = this.sut.AnalyzePasswordStrength("abcdefgh");

        Assert.Contains(MessageCode.TipAvoidSequences, analysis.Tips);
    }

    [Fact]
    public void AnalyzePasswordStrength_RepeatedCharacters_AddsReduceRepeatsTip()
    {
        var analysis = this.sut.AnalyzePasswordStrength("aaaaaaaa");

        Assert.Contains(MessageCode.TipReduceRepeats, analysis.Tips);
    }

    [Fact]
    public void AnalyzePasswordStrength_ContainsYear_AddsAvoidYearsTip()
    {
        // The year detector requires word boundaries around the year (\b(19|20)\d{2}\b),
        // so the digits must be flanked by non-word characters.
        var analysis = this.sut.AnalyzePasswordStrength("summer-1999!");

        Assert.Contains(MessageCode.TipAvoidYears, analysis.Tips);
    }

    [Fact]
    public void GeneratePassword_ReturnsRequestedLength()
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
    public void GeneratePassword_UppercaseOnly_ProducesOnlyUppercaseLetters()
    {
        var password = this.sut.GeneratePassword(40, PasswordGenerationOptions.IncludeUppercase);

        Assert.All(password, c => Assert.InRange(c, 'A', 'Z'));
    }

    [Fact]
    public void GeneratePassword_NumbersOnly_ProducesOnlyDigits()
    {
        var password = this.sut.GeneratePassword(40, PasswordGenerationOptions.IncludeNumbers);

        Assert.All(password, c => Assert.InRange(c, '0', '9'));
    }

    [Fact]
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

        Assert.DoesNotContain(password, c => ambiguous.Contains(c, StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratePassword_NoneOption_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => this.sut.GeneratePassword(16, PasswordGenerationOptions.None)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GeneratePassword_NonPositiveLength_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => this.sut.GeneratePassword(length, PasswordGenerationOptions.IncludeLowercase)
        );
    }

    [Fact]
    public void GeneratePassword_SuccessiveCalls_ProduceDifferentResults()
    {
        const PasswordGenerationOptions options =
            PasswordGenerationOptions.IncludeUppercase
            | PasswordGenerationOptions.IncludeLowercase
            | PasswordGenerationOptions.IncludeNumbers
            | PasswordGenerationOptions.IncludeSpecialCharacters;

        var first = this.sut.GeneratePassword(32, options);
        var second = this.sut.GeneratePassword(32, options);

        Assert.NotEqual(first, second);
    }
}
