namespace BackupZCrypt.Test.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Exceptions;

[TestFixture]
internal sealed class BackupExceptionTests
{
    [Test]
    public void EncryptionFileNotFoundException_SetsCodeAndMessage()
    {
        FileNotFoundException ex = new("test.txt");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(BackupErrorCode.FileNotFound));
            Assert.That(ex.Message, Does.Contain("test.txt"));
        }
    }

    [Test]
    public void EncryptionAccessDeniedException_SetsCodeAndInnerException()
    {
        Exception inner = new("denied");
        AccessDeniedException ex = new("secret.dat", inner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(BackupErrorCode.AccessDenied));
            Assert.That(ex.Message, Does.Contain("secret.dat"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }
    }

    [Test]
    public void EncryptionInsufficientSpaceException_SetsCode()
    {
        InsufficientSpaceException ex = new(@"D:\");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(BackupErrorCode.InsufficientDiskSpace));
            Assert.That(ex.Message, Does.Contain(@"D:\"));
        }
    }

    [Test]
    public void EncryptionInvalidPasswordException_SetsCode()
    {
        InvalidPasswordException ex = new();

        Assert.That(ex.Code, Is.EqualTo(BackupErrorCode.InvalidPassword));
    }

    [Test]
    public void EncryptionCorruptedFileException_SetsCode()
    {
        CorruptedFileException ex = new("corrupted.bzc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(BackupErrorCode.FileCorruption));
            Assert.That(ex.Message, Does.Contain("corrupted.bzc"));
        }
    }

    [Test]
    public void EncryptionKeyDerivationException_SetsCodeAndInnerException()
    {
        Exception inner = new("key error");
        KeyDerivationException ex = new(inner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(BackupErrorCode.KeyDerivationFailed));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }
    }

    [Test]
    public void EncryptionCipherException_SetsCodeAndMessage()
    {
        Exception inner = new("cipher error");
        CipherException ex = new("encryption", inner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Code, Is.EqualTo(BackupErrorCode.CipherOperationFailed));
            Assert.That(ex.Message, Does.Contain("encryption"));
            Assert.That(ex.InnerException, Is.SameAs(inner));
        }
    }
}
