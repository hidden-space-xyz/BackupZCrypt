using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Domain;

public sealed class BackupResultTests
{
    [Fact]
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

        Assert.True(result.HasErrors);
        Assert.True(result.HasWarnings);
    }

    [Fact]
    public void NoErrorsOrWarnings_DefaultToEmpty()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 0, 0, 0);

        Assert.False(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void FailedFiles_IsTotalMinusProcessed()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 100, 7, 10);

        Assert.Equal(3, result.FailedFiles);
    }

    [Fact]
    public void SuccessRate_ZeroFiles_IsOne()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 0, 0, 0);

        Assert.Equal(1.0, result.SuccessRate);
    }

    [Fact]
    public void SuccessRate_PartialProcessing_IsRatio()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 0, 2, 8);

        Assert.Equal(0.25, result.SuccessRate);
    }

    [Theory]
    [InlineData(0, 10, false)] // none processed
    [InlineData(5, 10, true)] // strictly between
    [InlineData(10, 10, false)] // all processed
    [InlineData(0, 0, false)] // empty
    public void IsPartialSuccess_OnlyTrueWhenStrictlyBetweenZeroAndTotal(
        int processed,
        int total,
        bool expected
    )
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(1), 0, processed, total);

        Assert.Equal(expected, result.IsPartialSuccess);
    }

    [Fact]
    public void BytesPerSecondAndFilesPerSecond_ZeroElapsed_ReturnZero()
    {
        var result = new BackupResult(true, TimeSpan.Zero, 1000, 5, 5);

        Assert.Equal(0, result.BytesPerSecond);
        Assert.Equal(0, result.FilesPerSecond);
    }

    [Fact]
    public void BytesPerSecondAndFilesPerSecond_NonZeroElapsed_ComputeRate()
    {
        var result = new BackupResult(true, TimeSpan.FromSeconds(2), 1000, 4, 4);

        Assert.Equal(500, result.BytesPerSecond);
        Assert.Equal(2, result.FilesPerSecond);
    }

    [Fact]
    public void Constructor_NegativeElapsed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupResult(true, TimeSpan.FromSeconds(-1), 0, 0, 0)
        );
    }

    [Fact]
    public void Constructor_NegativeTotalBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupResult(true, TimeSpan.Zero, -1, 0, 0)
        );
    }

    [Fact]
    public void Constructor_ProcessedGreaterThanTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupResult(true, TimeSpan.Zero, 0, 5, 3)
        );
    }
}
