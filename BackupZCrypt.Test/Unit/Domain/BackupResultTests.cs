using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Domain;

public sealed class BackupResultTests
{
    [Test]
    public void HasErrorsAndHasWarnings_ReflectSuppliedCollections()
    {
        var result = new BackupResult(
            isSuccess: false,
            elapsedTime: TimeSpan.FromSeconds(1),
            totalBytes: 0,
            processedFiles: 0,
            totalFiles: 0,
            errors: [new LocalizableMessage(MessageCode.AllFilesFailed)],
            warnings: [new LocalizableMessage(MessageCode.WeakPasswordWarning)]
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasErrors, Is.True);
            Assert.That(result.HasWarnings, Is.True);
        }
    }

    [Test]
    public void NoErrorsOrWarnings_DefaultToEmpty()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 0, 0, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.HasErrors, Is.False);
            Assert.That(result.HasWarnings, Is.False);
            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.Warnings, Is.Empty);
        }
    }

    [Test]
    public void FailedFiles_IsTotalMinusProcessed()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 100, 7, 10);

        Assert.That(result.FailedFiles, Is.EqualTo(3));
    }

    [Test]
    public void SuccessRate_ZeroFiles_IsOne()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 0, 0, 0);

        Assert.That(result.SuccessRate, Is.EqualTo(1.0));
    }

    [Test]
    public void SuccessRate_PartialProcessing_IsRatio()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 0, 2, 8);

        Assert.That(result.SuccessRate, Is.EqualTo(0.25));
    }

    [TestCase(0, 10, false)]
    [TestCase(5, 10, true)]
    [TestCase(10, 10, false)]
    [TestCase(0, 0, false)]
    public void IsPartialSuccess_OnlyTrueWhenStrictlyBetweenZeroAndTotal(
        int processed,
        int total,
        bool expected
    )
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 0, processed, total);

        Assert.That(result.IsPartialSuccess, Is.EqualTo(expected));
    }

    [Test]
    public void BytesPerSecondAndFilesPerSecond_ZeroElapsed_ReturnZero()
    {
        var result = new BackupResult(true, TimeSpan.Zero, 1000, 5, 5);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.BytesPerSecond, Is.Zero);
            Assert.That(result.FilesPerSecond, Is.Zero);
        }
    }

    [Test]
    public void BytesPerSecondAndFilesPerSecond_NonZeroElapsed_ComputeRate()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(2), 1000, 4, 4);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.BytesPerSecond, Is.EqualTo(500));
            Assert.That(result.FilesPerSecond, Is.EqualTo(2));
        }
    }

    [Test]
    public void Constructor_NegativeElapsed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupResult(true, TimeSpan.FromSeconds(-1), 0, 0, 0)
        );
    }

    [Test]
    public void Constructor_NegativeTotalBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupResult(true, TimeSpan.Zero, -1, 0, 0)
        );
    }

    [Test]
    public void Constructor_ProcessedGreaterThanTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupResult(true, TimeSpan.Zero, 0, 5, 3)
        );
    }
}
