namespace BackupZCrypt.Test.Infrastructure.Constants;

using BackupZCrypt.Infrastructure.Constants;

[TestFixture]
internal sealed class StreamConstantsTests
{
    [Test]
    public void CopyBufferSize_IsPositive()
    {
        Assert.That(StreamConstants.CopyBufferSize, Is.GreaterThan(0));
    }

    [Test]
    public void CopyBufferSize_Is80KB()
    {
        Assert.That(StreamConstants.CopyBufferSize, Is.EqualTo(80 * 1024));
    }
}
