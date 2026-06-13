using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.ValueObjects;

public class Result<T> : Result
{
    private readonly T? value;

    protected Result(T value, bool isSuccess, IReadOnlyList<LocalizableMessage> errors)
        : base(isSuccess, errors)
    {
        this.value = value;
    }

    public T Value =>
        this.IsSuccess
            ? this.value!
            : throw new InvalidOperationException("Cannot access the value of a failed result.");

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(MessageCode code) => Failure(code);

    public static new Result<T> Failure(params LocalizableMessage[] errors) =>
        new(default!, false, errors);

    public static new Result<T> Failure(MessageCode code, params object[] args) =>
        new(default!, false, [new LocalizableMessage(code, args)]);

    public static Result<T> Success(T value) => new(value, true, []);
}
