using BackupZCrypt.Application.Common;

namespace BackupZCrypt.Test.Unit.Application.Common;

/// <summary>
/// Unit tests for <see cref="UpdateSummary"/>, what an update did to the files a backup already held.
/// </summary>
public sealed class UpdateSummaryTests
{
    [Fact]
    internal void Constructor_Counts_AreKept()
    {
        UpdateSummary summary = new(7, 2);

        Assert.Multiple(
            () => Assert.Equal(7, summary.UnchangedFiles),
            () => Assert.Equal(2, summary.RemovedFiles)
        );
    }

    [Theory]
    [InlineData(-1, 0, "unchangedFiles")]
    [InlineData(0, -1, "removedFiles")]
    internal void Constructor_NegativeCount_ThrowsNamingTheOffendingParameter(
        int unchangedFiles,
        int removedFiles,
        string expectedParamName
    )
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new UpdateSummary(unchangedFiles, removedFiles)
        );

        Assert.Equal(expectedParamName, exception.ParamName);
    }
}
