namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Resources;

public abstract class EncryptionException : Exception
{
    protected EncryptionException(EncryptionErrorCode code, string? message = null)
        : base(message ?? code.ToString())
    {
        this.Code = code;
    }

    protected EncryptionException(
        EncryptionErrorCode code,
        string? message,
        Exception innerException)
        : base(message ?? code.ToString(), innerException)
    {
        this.Code = code;
    }

    public EncryptionErrorCode Code { get; }
}
