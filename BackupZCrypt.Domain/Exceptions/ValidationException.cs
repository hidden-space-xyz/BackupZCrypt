namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;

public class ValidationException : Exception
{
    public ValidationException(
        ValidationErrorCode code,
        string? message = null,
        string? paramName = null)
        : base(message ?? code.ToString())
    {
        this.Code = code;
        this.ParameterName = paramName;
    }

    public ValidationException()
    {
    }

    public ValidationException(string? message) : base(message)
    {
    }

    public ValidationException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    public ValidationErrorCode Code { get; }

    public string? ParameterName { get; }
}
