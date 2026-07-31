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
    [Test]
    public void Constructor_ValidInputs_StoresValuesInDeclaredOrder()
    {
        var status = new BackupStatus(3, 10, 300, 1000, TimeSpan.FromSeconds(2));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.ProcessedFiles, Is.EqualTo(3));
            Assert.That(status.TotalFiles, Is.EqualTo(10));
            Assert.That(status.ProcessedBytes, Is.EqualTo(300));
            Assert.That(status.TotalBytes, Is.EqualTo(1000));
            Assert.That(status.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(2)));
        }
    }

    [TestCase(-1, 10, 0L, 1000L, 0d, "processedFiles")]
    [TestCase(0, -1, 0L, 1000L, 0d, "totalFiles")]
    [TestCase(0, 10, -1L, 1000L, 0d, "processedBytes")]
    [TestCase(0, 10, 0L, -1L, 0d, "totalBytes")]
    [TestCase(0, 10, 0L, 1000L, -1d, "elapsed")]
    [TestCase(11, 10, 0L, 1000L, 0d, "processedFiles")]
    [TestCase(1, 10, 2000L, 1000L, 0d, "processedBytes")]
    public void Constructor_OutOfRangeArguments_ThrowsNamingTheOffendingParameter(
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

        Assert.That(exception?.ParamName, Is.EqualTo(expectedParamName));
    }
}
