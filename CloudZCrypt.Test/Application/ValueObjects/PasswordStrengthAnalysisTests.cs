namespace CloudZCrypt.Test.Application.ValueObjects;

using CloudZCrypt.Application.ValueObjects.Password;
using CloudZCrypt.Domain.Enums;

[TestFixture]
internal sealed class PasswordStrengthAnalysisTests
{
    [Test]
    public void Record_SetsAllProperties()
    {
        PasswordStrengthAnalysis analysis = new(PasswordStrength.Strong, "Strong password", 92.5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(analysis.Strength, Is.EqualTo(PasswordStrength.Strong));
            Assert.That(analysis.Description, Is.EqualTo("Strong password"));
            Assert.That(analysis.Score, Is.EqualTo(92.5));
        }
    }

    [Test]
    public void Record_EqualityByValue()
    {
        PasswordStrengthAnalysis a = new(PasswordStrength.Fair, "Fair", 50.0);
        PasswordStrengthAnalysis b = new(PasswordStrength.Fair, "Fair", 50.0);

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Record_InequalityByDifferentScore()
    {
        PasswordStrengthAnalysis a = new(PasswordStrength.Fair, "Fair", 50.0);
        PasswordStrengthAnalysis b = new(PasswordStrength.Fair, "Fair", 60.0);

        Assert.That(a, Is.Not.EqualTo(b));
    }
}
