using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.ValueObjects;

public class Result
{
    protected Result(bool isSuccess, IReadOnlyList<LocalizableMessage> errors)
    {
        this.IsSuccess = isSuccess;
        this.Errors = errors;
    }

    public IReadOnlyList<LocalizableMessage> Errors { get; }

    public bool IsSuccess { get; }

    public static implicit operator Result(MessageCode code) => Failure(code);

    public static Result Failure(params LocalizableMessage[] errors) => new(false, errors);

    public static Result Failure(MessageCode code, params object[] args) =>
        new(false, [new LocalizableMessage(code, args)]);
}
