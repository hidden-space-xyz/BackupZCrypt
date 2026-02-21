namespace BackupZCrypt.Test.Domain.ValueObjects;

using BackupZCrypt.Domain.Exceptions;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;

[TestFixture]
internal sealed class FileCryptStatusTests
{
    [Test]
    public void Constructor_WithValidInputs_SetsProperties()
    {
        FileCryptStatus status = new(2, 5, 512, 1024, TimeSpan.FromSeconds(3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.ProcessedFiles, Is.EqualTo(2));
            Assert.That(status.TotalFiles, Is.EqualTo(5));
            Assert.That(status.ProcessedBytes, Is.EqualTo(512));
            Assert.That(status.TotalBytes, Is.EqualTo(1024));
            Assert.That(status.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(3)));
        }
    }

    [Test]
    public void Constructor_NegativeProcessedFiles_Throws()
    {
        Assert.Throws<ValidationException>(() => new FileCryptStatus(-1, 5, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_NegativeTotalFiles_Throws()
    {
        Assert.Throws<ValidationException>(() => new FileCryptStatus(0, -1, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_NegativeProcessedBytes_Throws()
    {
        Assert.Throws<ValidationException>(() => new FileCryptStatus(0, 0, -1, 0, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_NegativeTotalBytes_Throws()
    {
        Assert.Throws<ValidationException>(() => new FileCryptStatus(0, 0, 0, -1, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_NegativeElapsed_Throws()
    {
        Assert.Throws<ValidationException>(() =>
            new FileCryptStatus(0, 0, 0, 0, TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void Constructor_ProcessedFilesExceedTotalFiles_Throws()
    {
        Assert.Throws<ValidationException>(() => new FileCryptStatus(6, 5, 0, 100, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_ProcessedBytesExceedTotalBytes_Throws()
    {
        Assert.Throws<ValidationException>(() =>
            new FileCryptStatus(0, 5, 200, 100, TimeSpan.Zero));
    }

    [Test]
    public void Constructor_AllZeros_Succeeds()
    {
        FileCryptStatus status = new(0, 0, 0, 0, TimeSpan.Zero);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.ProcessedFiles, Is.EqualTo(0));
            Assert.That(status.TotalFiles, Is.EqualTo(0));
        }
    }
}
