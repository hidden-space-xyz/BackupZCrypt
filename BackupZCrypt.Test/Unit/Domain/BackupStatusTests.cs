using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Test.Unit.Domain;

public sealed class BackupStatusTests
{
    [Test]
    public void Constructor_ValidInputs_StoresValues()
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

    [Test]
    public void Constructor_NegativeProcessedFiles_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupStatus(-1, 10, 0, 1000, TimeSpan.Zero)
        );
    }

    [Test]
    public void Constructor_NegativeElapsed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupStatus(1, 10, 0, 1000, TimeSpan.FromSeconds(-1))
        );
    }

    [Test]
    public void Constructor_ProcessedFilesGreaterThanTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupStatus(11, 10, 0, 1000, TimeSpan.Zero)
        );
    }

    [Test]
    public void Constructor_ProcessedBytesGreaterThanTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupStatus(1, 10, 2000, 1000, TimeSpan.Zero)
        );
    }
}
