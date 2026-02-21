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

    protected EncryptionException()
    {
    }

    protected EncryptionException(string? message) : base(message)
    {
    }

    protected EncryptionException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    public EncryptionErrorCode Code { get; }
}
