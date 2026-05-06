namespace BackupZCrypt.Test.Domain.ValueObjects;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Encryption;

[TestFixture]
internal sealed class EncryptionSessionTests
{
    [Test]
    public void Dispose_ZerosAllSensitiveBuffers()
    {
        byte[] salt = [0xFF, 0xAA, 0x55, 0x01];
        byte[] nonce = [0xFF, 0xBB, 0x66, 0x02];
        byte[] key = [0xFF, 0xCC, 0x77, 0x03];
        byte[] associatedData = [0xFF, 0xDD];
        EncryptionSession session = new(salt, nonce, key, CompressionMode.None, associatedData);

        session.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(key, Is.All.EqualTo(0));
            Assert.That(salt, Is.All.EqualTo(0));
            Assert.That(nonce, Is.All.EqualTo(0));
            Assert.That(associatedData, Is.All.EqualTo(0));
        }
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        EncryptionSession session = new([1, 2], [3, 4], [5, 6], CompressionMode.None, []);

        session.Dispose();

        Assert.DoesNotThrow(() => session.Dispose());
    }
}
