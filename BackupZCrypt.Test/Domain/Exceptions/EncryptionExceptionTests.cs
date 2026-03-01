namespace BackupZCrypt.Test.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Exceptions;

[TestFixture]
internal sealed class EncryptionExceptionTests
{
    [Test]
    public void EncryptionFileNotFoundException_SetsCodeAndMessage()
    {
        EncryptionFileNotFoundException ex = new("test.txt");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(EncryptionErrorCode.FileNotFound));
            Assert.That(ex.Message, Does.Contain("test.txt"));
        }
    }

    [Test]
    public void EncryptionAccessDeniedException_SetsCodeAndInnerException()
    {
        Exception inner = new("denied");
        EncryptionAccessDeniedException ex = new("secret.dat", inner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(EncryptionErrorCode.AccessDenied));
            Assert.That(ex.Message, Does.Contain("secret.dat"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }
    }

    [Test]
    public void EncryptionInsufficientSpaceException_SetsCode()
    {
        EncryptionInsufficientSpaceException ex = new(@"D:\");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(EncryptionErrorCode.InsufficientDiskSpace));
            Assert.That(ex.Message, Does.Contain(@"D:\"));
        }
    }

    [Test]
    public void EncryptionInvalidPasswordException_SetsCode()
    {
        EncryptionInvalidPasswordException ex = new();

        Assert.That(ex.Code, Is.EqualTo(EncryptionErrorCode.InvalidPassword));
    }

    [Test]
    public void EncryptionCorruptedFileException_SetsCode()
    {
        EncryptionCorruptedFileException ex = new("corrupted.bzc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(EncryptionErrorCode.FileCorruption));
            Assert.That(ex.Message, Does.Contain("corrupted.bzc"));
        }
    }

    [Test]
    public void EncryptionKeyDerivationException_SetsCodeAndInnerException()
    {
        Exception inner = new("key error");
        EncryptionKeyDerivationException ex = new(inner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(EncryptionErrorCode.KeyDerivationFailed));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }
    }

    [Test]
    public void EncryptionCipherException_SetsCodeAndMessage()
    {
        Exception inner = new("cipher error");
        EncryptionCipherException ex = new("encryption", inner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(EncryptionErrorCode.CipherOperationFailed));
            Assert.That(ex.Message, Does.Contain("encryption"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }
    }
}
