using System.Diagnostics.CodeAnalysis;

using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.ValueObjects;

/// <summary>
/// Represents the outcome of an application operation that produces a value on success,
/// or carries language-neutral error messages on failure.
/// </summary>
/// <typeparam name="T">The type of value produced on success.</typeparam>
public class Result<T> : Result
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Result{T}"/> class.
    /// </summary>
    /// <param name="value">
    /// The success value. It is still stored when <paramref name="isSuccess"/> is <see langword="false"/>, but
    /// <see cref="Value"/> then throws instead of returning it.
    /// </param>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="errors">The errors associated with a failure; empty on success.</param>
    protected Result(T value, bool isSuccess, IReadOnlyList<LocalizableMessage> errors)
        : base(isSuccess, errors)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the success value.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result represents a failure.</exception>
    [AllowNull]
    public T Value =>
        this.IsSuccess
            ? field!
            : throw new InvalidOperationException("Cannot access the value of a failed result.");

    /// <summary>
    /// Implicitly wraps a value in a successful result.
    /// </summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful result wrapping <paramref name="value"/>.</returns>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// Implicitly converts a message code into a failed result carrying that single error.
    /// </summary>
    /// <param name="code">The message code describing the failure.</param>
    /// <returns>A failed result carrying the message code as its only error.</returns>
    public static implicit operator Result<T>(MessageCode code) => Failure(code);

    /// <summary>
    /// Creates a failed result from the given error messages.
    /// </summary>
    /// <param name="errors">The errors describing the failure.</param>
    /// <returns>A failed result.</returns>
    public static new Result<T> Failure(params LocalizableMessage[] errors) =>
        new(default!, false, errors);

    /// <summary>
    /// Creates a failed result from a message code and its format arguments.
    /// </summary>
    /// <param name="code">The message code describing the failure.</param>
    /// <param name="args">The format arguments for the message.</param>
    /// <returns>A failed result.</returns>
    public static new Result<T> Failure(MessageCode code, params object[] args) =>
        new(default!, false, [new LocalizableMessage(code, args)]);

    /// <summary>
    /// Creates a successful result wrapping the given value.
    /// </summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Success(T value) => new(value, true, []);
}
