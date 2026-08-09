using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Test.Unit.Domain;

/// <summary>
/// Unit tests for the backup status value object.
/// </summary>
/// <remarks>
/// The constructor takes five positional parameters spanning only three distinct types, so the ordering
/// case gives each one a distinct value: the guards reject most swapped arguments, but a swapped
/// assignment inside the constructor would otherwise go unnoticed. The guard cases carry one row per
/// guard and assert <c>ParamName</c> rather than only the exception type, which keeps every row tied to
/// the guard it was written for.
/// </remarks>
public sealed class BackupStatusTests
{
    [Fact]
    internal void Constructor_ValidInputs_StoresValuesInDeclaredOrder()
    {
        var status = new BackupStatus(3, 10, 300, 1000, TimeSpan.FromSeconds(2));

        Assert.Multiple(
            () => Assert.Equal(3, status.ProcessedFiles),
            () => Assert.Equal(10, status.TotalFiles),
            () => Assert.Equal(300L, status.ProcessedBytes),
            () => Assert.Equal(1000L, status.TotalBytes),
            () => Assert.Equal(TimeSpan.FromSeconds(2), status.Elapsed)
        );
    }

    [Theory]
    [InlineData(-1, 10, 0L, 1000L, 0d, "processedFiles")]
    [InlineData(0, -1, 0L, 1000L, 0d, "totalFiles")]
    [InlineData(0, 10, -1L, 1000L, 0d, "processedBytes")]
    [InlineData(0, 10, 0L, -1L, 0d, "totalBytes")]
    [InlineData(0, 10, 0L, 1000L, -1d, "elapsed")]
    [InlineData(11, 10, 0L, 1000L, 0d, "processedFiles")]
    [InlineData(1, 10, 2000L, 1000L, 0d, "processedBytes")]
    internal void Constructor_OutOfRangeArguments_ThrowsNamingTheOffendingParameter(
        int processedFiles,
        int totalFiles,
        long processedBytes,
        long totalBytes,
        double elapsedSeconds,
        string expectedParamName
    )
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new BackupStatus(
                    processedFiles,
                    totalFiles,
                    processedBytes,
                    totalBytes,
                    TimeSpan.FromSeconds(elapsedSeconds)
                )
        );

        Assert.Equal(expectedParamName, exception.ParamName);
    }
}
