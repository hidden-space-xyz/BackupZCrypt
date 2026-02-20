namespace CloudZCrypt.Application.ValueObjects;

public class Result<T> : Result
{
    private readonly T? value;

    protected Result(T value, bool isSuccess, string[] errors)
        : base(isSuccess, errors)
    {
        this.value = value;
    }

    public T Value =>
        this.IsSuccess
            ? this.value!
            : throw new InvalidOperationException(
                Domain.Resources.Messages.CannotAccessFailedResultValue);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(string error) => Failure(error);

    public static new Result<T> Failure(params string[] errors) => new(default!, false, errors);

    public static Result<T> Success(T value) => new(value, true, Array.Empty<string>());
}
