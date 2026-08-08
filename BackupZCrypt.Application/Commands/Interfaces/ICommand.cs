using System.Diagnostics.CodeAnalysis;

namespace BackupZCrypt.Application.Commands.Interfaces;

/// <summary>
/// Marks a message that requests a state-changing operation producing a <typeparamref name="TResult"/>.
/// </summary>
/// <remarks>
/// The marker exists so <see cref="ICommandHandler{TCommand, TResult}"/> can constrain its command
/// type parameter, making it a compile error to bind a handler to a message whose declared result
/// type does not match the handler's.
/// </remarks>
/// <typeparam name="TResult">The type of result the command's handler produces.</typeparam>
[SuppressMessage(
    "Design",
    "CA1040:Avoid empty interfaces",
    Justification = "The marker ties a command to its result type through the handler's generic "
        + "constraint, which an attribute cannot do: constraints are the compile-time guarantee "
        + "that a handler only accepts messages declaring the result type it produces."
)]
[SuppressMessage(
    "Major Code Smell",
    "S2326:Unused type parameters should be removed",
    Justification = "Declaring the result type is the marker's entire job: the parameter is consumed "
        + "by the 'where TCommand : ICommand<TResult>' constraint on the handler, not by members of "
        + "this interface."
)]
public interface ICommand<TResult>;
