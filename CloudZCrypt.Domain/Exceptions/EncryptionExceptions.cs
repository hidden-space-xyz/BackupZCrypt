namespace CloudZCrypt.Domain.Exceptions;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Resources;

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
