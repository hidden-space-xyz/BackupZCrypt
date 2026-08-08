using System.Diagnostics.CodeAnalysis;

namespace BackupZCrypt.Application.Queries.Interfaces;

/// <summary>
/// Marks a message that requests a read-only operation producing a <typeparamref name="TResult"/>.
/// </summary>
/// <remarks>
/// The marker exists so <see cref="IQueryHandler{TQuery, TResult}"/> and
/// <see cref="ISyncQueryHandler{TQuery, TResult}"/> can constrain their query type parameter,
/// making it a compile error to bind a handler to a message whose declared result type does not
/// match the handler's.
/// </remarks>
/// <typeparam name="TResult">The type of result the query's handler produces.</typeparam>
[SuppressMessage(
    "Design",
    "CA1040:Avoid empty interfaces",
    Justification = "The marker ties a query to its result type through the handlers' generic "
        + "constraint, which an attribute cannot do: constraints are the compile-time guarantee "
        + "that a handler only accepts messages declaring the result type it produces."
)]
[SuppressMessage(
    "Major Code Smell",
    "S2326:Unused type parameters should be removed",
    Justification = "Declaring the result type is the marker's entire job: the parameter is consumed "
        + "by the 'where TQuery : IQuery<TResult>' constraint on the handlers, not by members of "
        + "this interface."
)]
public interface IQuery<TResult>;
