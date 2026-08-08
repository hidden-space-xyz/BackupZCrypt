namespace BackupZCrypt.Application.Queries.Interfaces;

/// <summary>
/// Handles a single query type, answering it without changing system state.
/// </summary>
/// <typeparam name="TQuery">The query type this handler answers.</typeparam>
/// <typeparam name="TResult">The type of result the handler produces.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>
    /// Answers the query.
    /// </summary>
    /// <param name="query">The query to answer.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of the query.</returns>
    public Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
