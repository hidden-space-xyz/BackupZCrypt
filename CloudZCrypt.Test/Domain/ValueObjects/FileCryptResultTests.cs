using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;

namespace CloudZCrypt.Test.Domain.ValueObjects;

[TestFixture]
internal sealed class FileCryptResultTests
{
    [Test]
    public void Constructor_WithValidInputs_SetsProperties()
    {
        TimeSpan elapsed = TimeSpan.FromSeconds(5);
        string[] errors = ["error1"];
        string[] warnings = ["warn1"];

        FileCryptResult result = new(true, elapsed, 1024, 3, 5, errors, warnings);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.ElapsedTime, Is.EqualTo(elapsed));
        Assert.That(result.TotalBytes, Is.EqualTo(1024));
        Assert.That(result.ProcessedFiles, Is.EqualTo(3));
        Assert.That(result.TotalFiles, Is.EqualTo(5));
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void Constructor_WithNullErrorsAndWarnings_DefaultsToEmpty()
    {
        FileCryptResult result = new(true, TimeSpan.Zero, 0, 0, 0);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Warnings, Is.Empty);
    }

    [Test]
    public void Constructor_NegativeElapsedTime_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            new FileCryptResult(true, TimeSpan.FromSeconds(-1), 0, 0, 0));
    }

    [Test]
    public void Constructor_NegativeTotalBytes_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            new FileCryptResult(true, TimeSpan.Zero, -1, 0, 0));
    }

    [Test]
    public void Constructor_NegativeProcessedFiles_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            new FileCryptResult(true, TimeSpan.Zero, 0, -1, 0));
    }

    [Test]
    public void Constructor_NegativeTotalFiles_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            new FileCryptResult(true, TimeSpan.Zero, 0, 0, -1));
    }

    [Test]
    public void Constructor_ProcessedFilesExceedTotalFiles_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            new FileCryptResult(true, TimeSpan.Zero, 0, 5, 3));
    }

    [Test]
    public void HasErrors_WithErrors_ReturnsTrue()
    {
        FileCryptResult result = new(false, TimeSpan.Zero, 0, 0, 0, errors: ["err"]);

        Assert.That(result.HasErrors, Is.True);
    }

    [Test]
    public void HasErrors_WithoutErrors_ReturnsFalse()
    {
        FileCryptResult result = new(true, TimeSpan.Zero, 0, 0, 0);

        Assert.That(result.HasErrors, Is.False);
    }

    [Test]
    public void HasWarnings_WithWarnings_ReturnsTrue()
    {
        FileCryptResult result = new(false, TimeSpan.Zero, 0, 0, 0, warnings: ["w"]);

        Assert.That(result.HasWarnings, Is.True);
    }

    [Test]
    public void FailedFiles_ReturnsCorrectCount()
    {
        FileCryptResult result = new(false, TimeSpan.FromSeconds(1), 100, 3, 5);

        Assert.That(result.FailedFiles, Is.EqualTo(2));
    }

    [Test]
    public void SuccessRate_AllProcessed_ReturnsOne()
    {
        FileCryptResult result = new(true, TimeSpan.FromSeconds(1), 100, 5, 5);

        Assert.That(result.SuccessRate, Is.EqualTo(1.0));
    }

    [Test]
    public void SuccessRate_ZeroTotal_ReturnsZero()
    {
        FileCryptResult result = new(true, TimeSpan.Zero, 0, 0, 0);

        Assert.That(result.SuccessRate, Is.EqualTo(0.0));
    }

    [Test]
    public void IsPartialSuccess_SomeProcessed_ReturnsTrue()
    {
        FileCryptResult result = new(false, TimeSpan.FromSeconds(1), 100, 3, 5);

        Assert.That(result.IsPartialSuccess, Is.True);
    }

    [Test]
    public void IsPartialSuccess_AllProcessed_ReturnsFalse()
    {
        FileCryptResult result = new(true, TimeSpan.FromSeconds(1), 100, 5, 5);

        Assert.That(result.IsPartialSuccess, Is.False);
    }

    [Test]
    public void BytesPerSecond_WithElapsedTime_CalculatesCorrectly()
    {
        FileCryptResult result = new(true, TimeSpan.FromSeconds(2), 1000, 1, 1);

        Assert.That(result.BytesPerSecond, Is.EqualTo(500.0));
    }

    [Test]
    public void BytesPerSecond_ZeroElapsed_ReturnsZero()
    {
        FileCryptResult result = new(true, TimeSpan.Zero, 1000, 1, 1);

        Assert.That(result.BytesPerSecond, Is.EqualTo(0));
    }

    [Test]
    public void FilesPerSecond_WithElapsedTime_CalculatesCorrectly()
    {
        FileCryptResult result = new(true, TimeSpan.FromSeconds(2), 100, 10, 10);

        Assert.That(result.FilesPerSecond, Is.EqualTo(5.0));
    }
}
