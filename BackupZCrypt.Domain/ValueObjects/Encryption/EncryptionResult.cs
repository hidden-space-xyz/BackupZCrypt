namespace BackupZCrypt.Domain.ValueObjects.Encryption;

using BackupZCrypt.Domain.Enums;

public class EncryptionResult
{
    protected EncryptionResult(bool isSuccess, BackupErrorCode? errorCode, string? errorMessage)
    {
        this.IsSuccess = isSuccess;
        this.ErrorCode = errorCode;
        this.ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !this.IsSuccess;

    public BackupErrorCode? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public bool IsFatalError =>
        this.ErrorCode is BackupErrorCode.AccessDenied
            or BackupErrorCode.InsufficientDiskSpace
            or BackupErrorCode.InvalidPassword
            or BackupErrorCode.KeyDerivationFailed;

    public static EncryptionResult Success() => new(true, null, null);

    public static EncryptionResult Failure(BackupErrorCode code, string message) =>
        new(false, code, message);
}
