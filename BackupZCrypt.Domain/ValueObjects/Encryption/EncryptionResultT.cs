namespace BackupZCrypt.Domain.ValueObjects.Encryption;

using BackupZCrypt.Domain.Enums;

public class EncryptionResult<T> : EncryptionResult
{
    private readonly T? value;

    private EncryptionResult(T value, bool isSuccess, BackupErrorCode? errorCode, string? errorMessage)
        : base(isSuccess, errorCode, errorMessage)
    {
        this.value = value;
    }

    public T Value =>
        this.IsSuccess
            ? this.value!
            : throw new InvalidOperationException(
                Resources.Messages.CannotAccessFailedResultValue);

    public static EncryptionResult<T> Success(T value) =>
        new(value, true, null, null);

    public static new EncryptionResult<T> Failure(BackupErrorCode code, string message) =>
        new(default!, false, code, message);
}
