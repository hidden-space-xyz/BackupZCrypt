namespace BackupZCrypt.Test.Domain.ValueObjects;

using BackupZCrypt.Domain.ValueObjects.Backup;

[TestFixture]
internal sealed class BackupStatusTests
{
    [Test]
    public void Constructor_NegativeProcessedFiles_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackupStatus(-1, 5, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_NegativeTotalFiles_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackupStatus(0, -1, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_NegativeProcessedBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackupStatus(0, 0, -1, 0, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_NegativeTotalBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackupStatus(0, 0, 0, -1, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_NegativeElapsed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BackupStatus(0, 0, 0, 0, TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void Constructor_ProcessedFilesExceedTotalFiles_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BackupStatus(6, 5, 0, 100, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_ProcessedBytesExceedTotalBytes_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BackupStatus(0, 5, 200, 100, TimeSpan.Zero));
    }
}
