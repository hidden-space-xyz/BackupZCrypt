namespace BackupZCrypt.Test.Domain.ValueObjects;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

[TestFixture]
internal sealed class BackupRequestTests
{
    [Test]
    public void DefaultOptionalParameters_HaveSafeDefaults()
    {
        BackupRequest request = new(
            @"C:\source",
            @"C:\dest",
            "pass",
            "pass",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            BackupOperation.Create);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Compression, Is.EqualTo(CompressionMode.None));
            Assert.That(request.ProceedOnWarnings, Is.False);
        }
    }
}
