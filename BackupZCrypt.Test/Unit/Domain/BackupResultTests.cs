using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Domain;

/// <summary>
/// Unit tests for the backup result value object.
/// </summary>
/// <remarks>
/// The guard cases assert <c>ParamName</c> rather than only the exception type, which covers all five
/// guards individually and pins that each one still reports the constructor parameter the caller passed.
/// </remarks>
public sealed class BackupResultTests
{
    [Test]
    public void ErrorAndWarningFlags_TrackSuppliedCollections()
    {
        var populated = new BackupResult(
            isSuccess: false,
            elapsedTime: TimeSpan.FromSeconds(1),
            totalBytes: 0,
            processedFiles: 0,
            totalFiles: 0,
            errors: [new LocalizableMessage(MessageCode.AllFilesFailed)],
            warnings: [new LocalizableMessage(MessageCode.WeakPasswordWarning)]
        );

        var omitted = new BackupResult(true, TimeSpan.FromSeconds(1), 0, 0, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(populated.HasErrors, Is.True);
            Assert.That(populated.HasWarnings, Is.True);
            Assert.That(populated.Errors.Select(e => e.Code), Is.EqualTo([MessageCode.AllFilesFailed]));
            Assert.That(
                populated.Warnings.Select(w => w.Code),
                Is.EqualTo([MessageCode.WeakPasswordWarning])
            );

            Assert.That(omitted.HasErrors, Is.False);
            Assert.That(omitted.HasWarnings, Is.False);
            Assert.That(omitted.Errors, Is.Empty);
            Assert.That(omitted.Warnings, Is.Empty);
        }
    }

    [TestCase(1d, 100L, 7, 10, 3, 0.7d, 100d, 7d)]
    [TestCase(1d, 0L, 2, 8, 6, 0.25d, 0d, 2d)]
    [TestCase(1d, 0L, 0, 0, 0, 1.0d, 0d, 0d)]
    [TestCase(0d, 1000L, 5, 5, 0, 1.0d, 0d, 0d)]
    [TestCase(2d, 1000L, 4, 4, 0, 1.0d, 500d, 2d)]
    public void DerivedMetrics_ComputeExpectedValues(
        double elapsedSeconds,
        long totalBytes,
        int processedFiles,
        int totalFiles,
        int expectedFailedFiles,
        double expectedSuccessRate,
        double expectedBytesPerSecond,
        double expectedFilesPerSecond
    )
    {
        var result = new BackupResult(
            true,
            TimeSpan.FromSeconds(elapsedSeconds),
            totalBytes,
            processedFiles,
            totalFiles
        );

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.FailedFiles, Is.EqualTo(expectedFailedFiles));
            Assert.That(result.SuccessRate, Is.EqualTo(expectedSuccessRate));
            Assert.That(result.BytesPerSecond, Is.EqualTo(expectedBytesPerSecond));
            Assert.That(result.FilesPerSecond, Is.EqualTo(expectedFilesPerSecond));
        }
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

    [TestCase(-1d, 0L, 0, 0, "elapsedTime")]
    [TestCase(0d, -1L, 0, 0, "totalBytes")]
    [TestCase(0d, 0L, -1, 0, "processedFiles")]
    [TestCase(0d, 0L, 0, -1, "totalFiles")]
    [TestCase(0d, 0L, 5, 3, "processedFiles")]
    public void Constructor_OutOfRangeArguments_ThrowsNamingTheOffendingParameter(
        double elapsedSeconds,
        long totalBytes,
        int processedFiles,
        int totalFiles,
        string expectedParamName
    )
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new BackupResult(
                    true,
                    TimeSpan.FromSeconds(elapsedSeconds),
                    totalBytes,
                    processedFiles,
                    totalFiles
                )
        );

        Assert.That(exception?.ParamName, Is.EqualTo(expectedParamName));
    }
}
