namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public class EncryptionKeyDerivationException : EncryptionException
{
    public EncryptionKeyDerivationException()
        : base(EncryptionErrorCode.KeyDerivationFailed)
    {
    }

    public EncryptionKeyDerivationException(string? message)
        : base(EncryptionErrorCode.KeyDerivationFailed, message)
    {
    }

    public EncryptionKeyDerivationException(string? message, Exception innerException)
        : base(EncryptionErrorCode.KeyDerivationFailed, message, innerException)
    {
    }

    public EncryptionKeyDerivationException(Exception innerException)
        : base(
            EncryptionErrorCode.KeyDerivationFailed,
            Messages.KeyDerivationFailed,
            innerException)
    {
    }

    protected EncryptionKeyDerivationException(
        EncryptionErrorCode code,
        string? message = null)
        : base(code, message)
    {
    }

    protected EncryptionKeyDerivationException(
        EncryptionErrorCode code,
        string? message,
        Exception innerException)
        : base(code, message, innerException)
    {
    }
}
