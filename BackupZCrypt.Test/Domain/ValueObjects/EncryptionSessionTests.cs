namespace BackupZCrypt.Test.Domain.ValueObjects;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Encryption;

[TestFixture]
internal sealed class EncryptionSessionTests
{
    [Test]
    public void Constructor_SetsAllProperties()
    {
        byte[] salt = [1, 2, 3];
        byte[] nonce = [4, 5, 6];
        byte[] key = [7, 8, 9];
        byte[] associatedData = [10, 11, 12];

        using EncryptionSession session = new(salt, nonce, key, CompressionMode.Zstd, associatedData);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(session.Salt, Is.SameAs(salt));
            Assert.That(session.Nonce, Is.SameAs(nonce));
            Assert.That(session.Key, Is.SameAs(key));
            Assert.That(session.Compression, Is.EqualTo(CompressionMode.Zstd));
            Assert.That(session.AssociatedData, Is.SameAs(associatedData));
        }
    }

    [Test]
    public void Dispose_ZerosKey()
    {
        byte[] key = [0xFF, 0xAA, 0x55, 0x01];
        EncryptionSession session = new([1], [2], key, CompressionMode.None, []);

        session.Dispose();

        Assert.That(key, Is.All.EqualTo(0));
    }

    [Test]
    public void Dispose_ZerosSalt()
    {
        byte[] salt = [0xFF, 0xAA, 0x55, 0x01];
        EncryptionSession session = new(salt, [2], [3], CompressionMode.None, []);

        session.Dispose();

        Assert.That(salt, Is.All.EqualTo(0));
    }

    [Test]
    public void Dispose_ZerosNonce()
    {
        byte[] nonce = [0xFF, 0xAA, 0x55, 0x01];
        EncryptionSession session = new([1], nonce, [3], CompressionMode.None, []);

        session.Dispose();

        Assert.That(nonce, Is.All.EqualTo(0));
    }

    [Test]
    public void Dispose_ZerosAssociatedData()
    {
        byte[] associatedData = [0xFF, 0xAA];
        EncryptionSession session = new([1], [2], [3], CompressionMode.None, associatedData);

        session.Dispose();

        Assert.That(associatedData, Is.All.EqualTo(0));
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        EncryptionSession session = new([1, 2], [3, 4], [5, 6], CompressionMode.None, []);

        session.Dispose();

        Assert.DoesNotThrow(() => session.Dispose());
    }
}
