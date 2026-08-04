using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.ValueObjects;

/// <summary>
/// Represents the outcome of an application operation as either success or failure with
/// a list of language-neutral error messages, avoiding exceptions for expected failures.
/// </summary>
public class Result
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Result"/> class.
    /// </summary>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="errors">The errors associated with a failure; empty on success.</param>
    protected Result(bool isSuccess, IReadOnlyList<LocalizableMessage> errors)
    {
        this.IsSuccess = isSuccess;
        this.Errors = errors;
    }

    /// <summary>
    /// Gets the localizable errors describing why the operation failed; empty on success.
    /// </summary>
    public IReadOnlyList<LocalizableMessage> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Implicitly converts a message code into a failed result carrying that single error.
    /// </summary>
    /// <param name="code">The message code describing the failure.</param>
    /// <returns>A failed result carrying the message code as its only error.</returns>
    public static implicit operator Result(MessageCode code)
    {
        return Failure(code);
    }

    /// <summary>
    /// Creates a failed result from the given error messages.
    /// </summary>
    /// <param name="errors">The errors describing the failure.</param>
    /// <returns>A failed result.</returns>
    public static Result Failure(params LocalizableMessage[] errors)
    {
        return new(false, errors);
    }

    /// <summary>
    /// Creates a failed result from a message code and its format arguments.
    /// </summary>
    /// <param name="code">The message code describing the failure.</param>
    /// <param name="args">The format arguments for the message.</param>
    /// <returns>A failed result.</returns>
    public static Result Failure(MessageCode code, params object[] args)
    {
        return new(false, [new LocalizableMessage(code, args)]);
    }
}
