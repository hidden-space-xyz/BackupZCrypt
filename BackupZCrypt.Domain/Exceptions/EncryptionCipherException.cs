namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionCipherException : EncryptionException
{
    public EncryptionCipherException()
        : base(EncryptionErrorCode.CipherOperationFailed)
    {
    }

    public EncryptionCipherException(string? message)
        : base(EncryptionErrorCode.CipherOperationFailed, message)
    {
    }

    public EncryptionCipherException(string? message, Exception innerException)
        : base(EncryptionErrorCode.CipherOperationFailed, message, innerException)
    {
    }

    public static EncryptionCipherException CreateForOperation(
        string operation,
        Exception innerException) =>
        new(string.Format(Messages.CipherOperationFailedFormat, operation), innerException);

    protected EncryptionCipherException(
        EncryptionErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected EncryptionCipherException(
        EncryptionErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
