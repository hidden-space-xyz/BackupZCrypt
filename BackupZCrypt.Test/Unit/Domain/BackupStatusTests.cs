using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Test.Unit.Domain;

public sealed class BackupStatusTests
{
    [Fact]
    public void Constructor_ValidInputs_StoresValues()
    {
        var status = new BackupStatus(3, 10, 300, 1000, TimeSpan.FromSeconds(2));

        Assert.Equal(3, status.ProcessedFiles);
        Assert.Equal(10, status.TotalFiles);
        Assert.Equal(300, status.ProcessedBytes);
        Assert.Equal(1000, status.TotalBytes);
        Assert.Equal(TimeSpan.FromSeconds(2), status.Elapsed);
    }

    [Fact]
    public void Constructor_NegativeProcessedFiles_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupStatus(-1, 10, 0, 1000, TimeSpan.Zero)
        );
    }

    [Fact]
    public void Constructor_NegativeElapsed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupStatus(1, 10, 0, 1000, TimeSpan.FromSeconds(-1))
        );
    }

    [Fact]
    public void Constructor_ProcessedFilesGreaterThanTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupStatus(11, 10, 0, 1000, TimeSpan.Zero)
        );
    }

    [Fact]
    public void Constructor_ProcessedBytesGreaterThanTotal_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BackupStatus(1, 10, 2000, 1000, TimeSpan.Zero)
        );
    }
}
