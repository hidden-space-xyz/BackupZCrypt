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
    [Fact]
    internal void ErrorAndWarningFlags_TrackSuppliedCollections()
    {
        var populated = new BackupResult(
            elapsedTime: TimeSpan.FromSeconds(1),
            totalBytes: 0,
            processedFiles: 0,
            totalFiles: 0,
            errors: [new LocalizableMessage(MessageCode.AllFilesFailed)],
            warnings: [new LocalizableMessage(MessageCode.WeakPasswordWarning)]
        );

        var omitted = new BackupResult(TimeSpan.FromSeconds(1), 0, 0, 0);

        Assert.Multiple(
            () => Assert.True(populated.HasErrors),
            () => Assert.True(populated.HasWarnings),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.AllFilesFailed],
                    populated.Errors.Select(e => e.Code)
                ),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.WeakPasswordWarning],
                    populated.Warnings.Select(w => w.Code)
                ),
            () => Assert.False(omitted.HasErrors),
            () => Assert.False(omitted.HasWarnings),
            () => Assert.Empty(omitted.Errors),
            () => Assert.Empty(omitted.Warnings)
        );
    }

    [Theory]
    [InlineData(5, 5, false, true)]
    [InlineData(5, 5, true, false)]
    [InlineData(3, 5, false, false)]
    [InlineData(0, 0, false, true)]
    [InlineData(0, 0, true, false)]
    internal void IsSuccess_DerivesFromTheRecordedErrorsAndTheFileCounts(
        int processedFiles,
        int totalFiles,
        bool withError,
        bool expected
    )
    {
        LocalizableMessage[]? errors = withError
            ? [new LocalizableMessage(MessageCode.AllFilesFailed)]
            : null;

        var result = new BackupResult(
            TimeSpan.FromSeconds(1),
            0,
            processedFiles,
            totalFiles,
            errors: errors
        );

        Assert.Equal(expected, result.IsSuccess);
    }

    [Fact]
    internal void IsSuccess_WithWarningsButNoErrors_RemainsTrue()
    {
        var result = new BackupResult(
            TimeSpan.FromSeconds(1),
            0,
            5,
            5,
            warnings: [new LocalizableMessage(MessageCode.WeakPasswordWarning)]
        );

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(1d, 100L, 7, 10, 3, 0.7d, 100d, 7d)]
    [InlineData(1d, 0L, 2, 8, 6, 0.25d, 0d, 2d)]
    [InlineData(1d, 0L, 0, 0, 0, 1.0d, 0d, 0d)]
    [InlineData(0d, 1000L, 5, 5, 0, 1.0d, 0d, 0d)]
    [InlineData(2d, 1000L, 4, 4, 0, 1.0d, 500d, 2d)]
    internal void DerivedMetrics_ComputeExpectedValues(
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
            TimeSpan.FromSeconds(elapsedSeconds),
            totalBytes,
            processedFiles,
            totalFiles
        );

        Assert.Multiple(
            () => Assert.Equal(expectedFailedFiles, result.FailedFiles),
            () => Assert.Equal(expectedSuccessRate, result.SuccessRate),
            () => Assert.Equal(expectedBytesPerSecond, result.BytesPerSecond),
            () => Assert.Equal(expectedFilesPerSecond, result.FilesPerSecond)
        );
    }

    [Theory]
    [InlineData(0, 10, false)]
    [InlineData(5, 10, true)]
    [InlineData(10, 10, false)]
    [InlineData(0, 0, false)]
    internal void IsPartialSuccess_OnlyTrueWhenStrictlyBetweenZeroAndTotal(
        int processed,
        int total,
        bool expected
    )
    {
        var result = new BackupResult(TimeSpan.FromSeconds(1), 0, processed, total);

        Assert.Equal(expected, result.IsPartialSuccess);
    }

    [Theory]
    [InlineData(-1d, 0L, 0, 0, "elapsedTime")]
    [InlineData(0d, -1L, 0, 0, "totalBytes")]
    [InlineData(0d, 0L, -1, 0, "processedFiles")]
    [InlineData(0d, 0L, 0, -1, "totalFiles")]
    [InlineData(0d, 0L, 5, 3, "processedFiles")]
    internal void Constructor_OutOfRangeArguments_ThrowsNamingTheOffendingParameter(
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
                    TimeSpan.FromSeconds(elapsedSeconds),
                    totalBytes,
                    processedFiles,
                    totalFiles
                )
        );

        Assert.Equal(expectedParamName, exception.ParamName);
    }
}
