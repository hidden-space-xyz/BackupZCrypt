namespace CloudZCrypt.Application.ValueObjects;

public class Result
{
    protected Result(bool isSuccess, string[] errors)
    {
        this.IsSuccess = isSuccess;
        this.Errors = errors;
    }

    public string[] Errors { get; }

    public bool IsSuccess { get; }

    public static implicit operator Result(string error) => Failure(error);

    public static Result Failure(params string[] errors) => new(false, errors);
}
