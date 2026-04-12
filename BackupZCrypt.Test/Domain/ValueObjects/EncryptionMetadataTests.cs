namespace BackupZCrypt.Test.Domain.ValueObjects;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Encryption;

[TestFixture]
internal sealed class EncryptionMetadataTests
{
    [Test]
    public void Record_SetsAllProperties()
    {
        byte[] salt = [1, 2, 3];
        byte[] nonce = [4, 5, 6];

        EncryptionMetadata metadata = new(salt, nonce, CompressionMode.ZstdBest);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata.Salt, Is.SameAs(salt));
            Assert.That(metadata.Nonce, Is.SameAs(nonce));
            Assert.That(metadata.Compression, Is.EqualTo(CompressionMode.ZstdBest));
        }
    }

    [Test]
    public void Record_EqualityByValue()
    {
        byte[] salt = [1, 2, 3];
        byte[] nonce = [4, 5, 6];

        EncryptionMetadata a = new(salt, nonce, CompressionMode.Zstd);
        EncryptionMetadata b = new(salt, nonce, CompressionMode.Zstd);

        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void Record_InequalityByDifferentCompression()
    {
        byte[] salt = [1, 2, 3];
        byte[] nonce = [4, 5, 6];

        EncryptionMetadata a = new(salt, nonce, CompressionMode.Zstd);
        EncryptionMetadata b = new(salt, nonce, CompressionMode.ZstdBest);

        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void Record_InequalityByDifferentSaltReference()
    {
        EncryptionMetadata a = new([1, 2], [3, 4], CompressionMode.None);
        EncryptionMetadata b = new([1, 2], [3, 4], CompressionMode.None);

        Assert.That(a, Is.Not.EqualTo(b));
    }
}
