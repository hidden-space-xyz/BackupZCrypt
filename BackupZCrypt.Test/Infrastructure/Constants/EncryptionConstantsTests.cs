namespace BackupZCrypt.Test.Infrastructure.Constants;

using BackupZCrypt.Infrastructure.Constants;

[TestFixture]
internal sealed class EncryptionConstantsTests
{
    [Test]
    public void KeySize_Is256()
    {
        Assert.That(EncryptionConstants.KeySize, Is.EqualTo(256));
    }

    [Test]
    public void SaltSize_Is32()
    {
        Assert.That(EncryptionConstants.SaltSize, Is.EqualTo(32));
    }

    [Test]
    public void NonceSize_Is12()
    {
        Assert.That(EncryptionConstants.NonceSize, Is.EqualTo(12));
    }

    [Test]
    public void CompressionHeaderSize_Is1()
    {
        Assert.That(EncryptionConstants.CompressionHeaderSize, Is.EqualTo(1));
    }

    [Test]
    public void MacSize_Is128()
    {
        Assert.That(EncryptionConstants.MacSize, Is.EqualTo(128));
    }

    [Test]
    public void BufferSize_Is80KB()
    {
        Assert.That(EncryptionConstants.BufferSize, Is.EqualTo(80 * 1024));
    }

    [Test]
    public void HeaderSize_IsSumOfSaltNonceAndCompressionHeader()
    {
        const int expected = EncryptionConstants.SaltSize
            + EncryptionConstants.NonceSize
            + EncryptionConstants.CompressionHeaderSize;

        Assert.That(expected, Is.EqualTo(EncryptionConstants.HeaderSize));
    }

    [Test]
    public void HeaderSize_Is45()
    {
        Assert.That(EncryptionConstants.HeaderSize, Is.EqualTo(45));
    }
}
