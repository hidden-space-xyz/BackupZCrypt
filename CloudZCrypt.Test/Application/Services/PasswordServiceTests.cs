namespace CloudZCrypt.Test.Application.Services;

using CloudZCrypt.Application.Services;
using CloudZCrypt.Application.ValueObjects.Password;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;

[TestFixture]
internal sealed class PasswordServiceTests
{
    private PasswordService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new PasswordService();
    }

    [Test]
    public void AnalyzePasswordStrength_EmptyPassword_ReturnsVeryWeak()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength(string.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strength, Is.EqualTo(PasswordStrength.VeryWeak));
            Assert.That(result.Score, Is.EqualTo(0));
        }
    }

    [Test]
    public void AnalyzePasswordStrength_NullPassword_ReturnsVeryWeak()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength(null!);

        Assert.That(result.Strength, Is.EqualTo(PasswordStrength.VeryWeak));
    }

    [Test]
    public void AnalyzePasswordStrength_ShortSimplePassword_ReturnsLowScore()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength("abc");

        Assert.That(result.Score, Is.LessThan(30));
    }

    [Test]
    public void AnalyzePasswordStrength_StrongPassword_ReturnsHighScore()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength("Kj9$mP2!xL7&nQ4#wR8@");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Strength, Is.EqualTo(PasswordStrength.Strong));
            Assert.That(result.Score, Is.GreaterThanOrEqualTo(85));
        }
    }

    [Test]
    public void AnalyzePasswordStrength_OnlyLowercase_HasLowScore()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength("abcdefghij");

        Assert.That(result.Score, Is.LessThan(50));
    }

    [Test]
    public void AnalyzePasswordStrength_RepeatedCharacters_PenalizedScore()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength("aaaaaaaaaa");

        Assert.That(result.Score, Is.LessThan(20));
    }

    [Test]
    public void AnalyzePasswordStrength_CommonPatterns_PenalizedScore()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength("password123");

        Assert.That(result.Strength, Is.Not.EqualTo(PasswordStrength.Strong));
    }

    [Test]
    public void AnalyzePasswordStrength_SequentialChars_PenalizedScore()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength("abcdefgh");

        Assert.That(result.Score, Is.LessThan(50));
    }

    [Test]
    public void AnalyzePasswordStrength_MixedCharacterTypes_BetterScore()
    {
        PasswordStrengthAnalysis lowResult = this.service.AnalyzePasswordStrength("abcdefghij");
        PasswordStrengthAnalysis highResult = this.service.AnalyzePasswordStrength("aBc1dE!gHi");

        Assert.That(highResult.Score, Is.GreaterThan(lowResult.Score));
    }

    [Test]
    public void AnalyzePasswordStrength_DescriptionContainsStrengthLabel()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength("Kj9$mP2!xL7&nQ4#wR8@");

        Assert.That(result.Description, Does.Contain("Strong"));
    }

    [Test]
    public void AnalyzePasswordStrength_ScoreIsBetweenZeroAndHundred()
    {
        PasswordStrengthAnalysis result = this.service.AnalyzePasswordStrength("anyPassword!123");

        Assert.That(result.Score, Is.GreaterThanOrEqualTo(0));
        Assert.That(result.Score, Is.LessThanOrEqualTo(100));
    }

    [Test]
    public void GeneratePassword_ValidParameters_ReturnsCorrectLength()
    {
        string password = this.service.GeneratePassword(
            16,
            PasswordGenerationOptions.IncludeUppercase | PasswordGenerationOptions.IncludeLowercase);

        Assert.That(password, Has.Length.EqualTo(16));
    }

    [Test]
    public void GeneratePassword_IncludeNumbers_ContainsDigits()
    {
        string password = this.service.GeneratePassword(50, PasswordGenerationOptions.IncludeNumbers);

        Assert.That(password.Any(char.IsDigit), Is.True);
    }

    [Test]
    public void GeneratePassword_IncludeUppercase_ContainsUppercase()
    {
        string password = this.service.GeneratePassword(50, PasswordGenerationOptions.IncludeUppercase);

        Assert.That(password.Any(char.IsUpper), Is.True);
    }

    [Test]
    public void GeneratePassword_IncludeLowercase_ContainsLowercase()
    {
        string password = this.service.GeneratePassword(50, PasswordGenerationOptions.IncludeLowercase);

        Assert.That(password.Any(char.IsLower), Is.True);
    }

    [Test]
    public void GeneratePassword_IncludeSpecialChars_ContainsSpecial()
    {
        string password = this.service.GeneratePassword(
            50,
            PasswordGenerationOptions.IncludeSpecialCharacters);

        Assert.That(password.Any(c => !char.IsLetterOrDigit(c)), Is.True);
    }

    [Test]
    public void GeneratePassword_ExcludeSimilar_DoesNotContainSimilarChars()
    {
        string password = this.service.GeneratePassword(
            100,
            PasswordGenerationOptions.IncludeUppercase
                | PasswordGenerationOptions.IncludeLowercase
                | PasswordGenerationOptions.IncludeNumbers
                | PasswordGenerationOptions.ExcludeSimilarCharacters);

        const string similarChars = "il1Lo0O";
        Assert.That(password.Any(similarChars.Contains), Is.False);
    }

    [Test]
    public void GeneratePassword_ZeroLength_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            this.service.GeneratePassword(0, PasswordGenerationOptions.IncludeLowercase));
    }

    [Test]
    public void GeneratePassword_NegativeLength_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            this.service.GeneratePassword(-5, PasswordGenerationOptions.IncludeLowercase));
    }

    [Test]
    public void GeneratePassword_NoOptions_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            this.service.GeneratePassword(10, PasswordGenerationOptions.None));
    }

    [Test]
    public void GeneratePassword_TwoCallsProduceDifferentResults()
    {
        string p1 = this.service.GeneratePassword(
            32,
            PasswordGenerationOptions.IncludeUppercase | PasswordGenerationOptions.IncludeLowercase);
        string p2 = this.service.GeneratePassword(
            32,
            PasswordGenerationOptions.IncludeUppercase | PasswordGenerationOptions.IncludeLowercase);

        Assert.That(p1, Is.Not.EqualTo(p2));
    }
}
