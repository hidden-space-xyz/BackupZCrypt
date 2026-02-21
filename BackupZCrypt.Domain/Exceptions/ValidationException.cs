namespace BackupZCrypt.Domain.Exceptions;

using BackupZCrypt.Domain.Enums;

public class ValidationException(
    ValidationErrorCode code,
    string? message = null,
    string? paramName = null) : Exception(message ?? code.ToString())
{
    public ValidationErrorCode Code { get; } = code;

    public string? ParameterName { get; } = paramName;
}
