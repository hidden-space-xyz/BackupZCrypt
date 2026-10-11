using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Algorithms;

/// <summary>
/// Looks up the registered strategy that implements a given algorithm or mode.
/// </summary>
/// <remarks>
/// Strategies are stateless singletons, so a lookup returns the registered instance rather than
/// creating one. An identifier with no registered strategy is an error rather than a fallback:
/// substituting another algorithm would write or read an archive under one its manifest does not name.
/// </remarks>
/// <typeparam name="TId">The enum whose members name the implementations of the strategy family.</typeparam>
/// <typeparam name="TStrategy">The strategy family looked up.</typeparam>
internal interface IStrategyRegistry<TId, out TStrategy>
    where TId : struct, Enum
    where TStrategy : IAlgorithmStrategy<TId>
{
    /// <summary>
    /// Gets the identifiers a strategy is registered for, in identifier order; never empty.
    /// </summary>
    public IReadOnlyList<TId> Ids { get; }

    /// <summary>
    /// Returns the strategy registered for the specified identifier.
    /// </summary>
    /// <param name="id">The algorithm or mode to look up.</param>
    /// <returns>The registered strategy whose <see cref="IAlgorithmStrategy{TId}.Id"/> is <paramref name="id"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No strategy is registered for <paramref name="id"/>.</exception>
    public TStrategy Resolve(TId id);
}
