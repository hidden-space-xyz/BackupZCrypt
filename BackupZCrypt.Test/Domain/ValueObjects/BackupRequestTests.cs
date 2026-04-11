namespace BackupZCrypt.Test.Domain.ValueObjects;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

[TestFixture]
internal sealed class BackupRequestTests
{
    [Test]
    public void Record_SetsAllProperties()
    {
        BackupRequest request = new(
            SourcePath: @"C:\source",
            DestinationPath: @"C:\dest",
            Password: "password123",
            ConfirmPassword: "password123",
            EncryptionAlgorithm: EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm: KeyDerivationAlgorithm.Argon2id,
            Operation: EncryptOperation.Encrypt,
            NameObfuscation: NameObfuscationMode.None,
            Compression: CompressionMode.Zstd,
            ProceedOnWarnings: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.SourcePath, Is.EqualTo(@"C:\source"));
            Assert.That(request.DestinationPath, Is.EqualTo(@"C:\dest"));
            Assert.That(request.Password, Is.EqualTo("password123"));
            Assert.That(request.ConfirmPassword, Is.EqualTo("password123"));
            Assert.That(request.EncryptionAlgorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
            Assert.That(request.KeyDerivationAlgorithm, Is.EqualTo(KeyDerivationAlgorithm.Argon2id));
            Assert.That(request.Operation, Is.EqualTo(EncryptOperation.Encrypt));
            Assert.That(request.NameObfuscation, Is.EqualTo(NameObfuscationMode.None));
            Assert.That(request.Compression, Is.EqualTo(CompressionMode.Zstd));
            Assert.That(request.ProceedOnWarnings, Is.True);
        }
    }

    [Test]
    public void Record_DefaultCompression_IsNone()
    {
        BackupRequest request = new(
            @"C:\source",
            @"C:\dest",
            "pass",
            "pass",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(request.Compression, Is.EqualTo(CompressionMode.None));
            Assert.That(request.ProceedOnWarnings, Is.False);
        }
    }

    [Test]
    public void Record_WithExpression_CreatesModifiedCopy()
    {
        BackupRequest original = new(
            @"C:\source",
            @"C:\dest",
            "pass",
            "pass",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None);

        BackupRequest modified = original with { Operation = EncryptOperation.Decrypt };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(modified.Operation, Is.EqualTo(EncryptOperation.Decrypt));
            Assert.That(modified.SourcePath, Is.EqualTo(original.SourcePath));
        }
    }
}
