namespace BackupZCrypt.Application.Queries.Interfaces;

/// <summary>
/// Handles a single query type synchronously, for pure in-memory computations a caller may need on
/// the UI thread, such as per-keystroke password analysis.
/// </summary>
/// <typeparam name="TQuery">The query type this handler answers.</typeparam>
/// <typeparam name="TResult">The type of result the handler produces.</typeparam>
public interface ISyncQueryHandler<in TQuery, out TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>
    /// Answers the query synchronously.
    /// </summary>
    /// <param name="query">The query to answer.</param>
    /// <returns>The result of the query.</returns>
    public TResult Handle(TQuery query);
}
