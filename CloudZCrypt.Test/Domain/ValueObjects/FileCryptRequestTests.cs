using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;

namespace CloudZCrypt.Test.Domain.ValueObjects;

[TestFixture]
internal sealed class FileCryptRequestTests
{
    [Test]
    public void Record_SetsAllProperties()
    {
        FileCryptRequest request = new(
            SourcePath: @"C:\source",
            DestinationPath: @"C:\dest",
            Password: "password123",
            ConfirmPassword: "password123",
            EncryptionAlgorithm: EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm: KeyDerivationAlgorithm.Argon2id,
            Operation: EncryptOperation.Encrypt,
            NameObfuscation: NameObfuscationMode.None,
            Compression: CompressionMode.GZip,
            ProceedOnWarnings: true
        );

        Assert.That(request.SourcePath, Is.EqualTo(@"C:\source"));
        Assert.That(request.DestinationPath, Is.EqualTo(@"C:\dest"));
        Assert.That(request.Password, Is.EqualTo("password123"));
        Assert.That(request.ConfirmPassword, Is.EqualTo("password123"));
        Assert.That(request.EncryptionAlgorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
        Assert.That(request.KeyDerivationAlgorithm, Is.EqualTo(KeyDerivationAlgorithm.Argon2id));
        Assert.That(request.Operation, Is.EqualTo(EncryptOperation.Encrypt));
        Assert.That(request.NameObfuscation, Is.EqualTo(NameObfuscationMode.None));
        Assert.That(request.Compression, Is.EqualTo(CompressionMode.GZip));
        Assert.That(request.ProceedOnWarnings, Is.True);
    }

    [Test]
    public void Record_DefaultCompression_IsNone()
    {
        FileCryptRequest request = new(
            @"C:\source", @"C:\dest", "pass", "pass",
            EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt, NameObfuscationMode.None
        );

        Assert.That(request.Compression, Is.EqualTo(CompressionMode.None));
        Assert.That(request.ProceedOnWarnings, Is.False);
    }

    [Test]
    public void Record_WithExpression_CreatesModifiedCopy()
    {
        FileCryptRequest original = new(
            @"C:\source", @"C:\dest", "pass", "pass",
            EncryptionAlgorithm.Aes, KeyDerivationAlgorithm.PBKDF2,
            EncryptOperation.Encrypt, NameObfuscationMode.None
        );

        FileCryptRequest modified = original with { Operation = EncryptOperation.Decrypt };

        Assert.That(modified.Operation, Is.EqualTo(EncryptOperation.Decrypt));
        Assert.That(modified.SourcePath, Is.EqualTo(original.SourcePath));
    }
}
