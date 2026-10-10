using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Strategies;

/// <summary>
/// Indexes the registered strategies of one family by the identifier each one implements.
/// </summary>
/// <typeparam name="TId">The enum whose members name the implementations of the strategy family.</typeparam>
/// <typeparam name="TStrategy">The strategy family indexed.</typeparam>
internal sealed class StrategyRegistry<TId, TStrategy> : IStrategyRegistry<TId, TStrategy>
    where TId : struct, Enum
    where TStrategy : IAlgorithmStrategy<TId>
{
    /// <summary>
    /// The lookup table of registered strategies keyed by the identifier each one implements.
    /// </summary>
    private readonly Dictionary<TId, TStrategy> strategies;

    /// <summary>
    /// Initializes a new instance of the <see cref="StrategyRegistry{TId, TStrategy}"/> class.
    /// </summary>
    /// <remarks>
    /// An empty family is rejected because it can only mean the registry was closed over a type no
    /// strategy is registered against, which would otherwise surface as "not registered" on the first
    /// lookup instead of when the container builds the consumer.
    /// </remarks>
    /// <param name="strategies">The registered strategies of the family.</param>
    /// <exception cref="ArgumentException">Two strategies implement the same identifier.</exception>
    /// <exception cref="InvalidOperationException">No strategy of the family is registered.</exception>
    public StrategyRegistry(IEnumerable<TStrategy> strategies)
    {
        this.strategies = strategies.ToDictionary(static s => s.Id);

        if (this.strategies.Count is 0)
        {
            throw new InvalidOperationException($"No {typeof(TStrategy).Name} is registered.");
        }
    }

    /// <inheritdoc/>
    public TStrategy Resolve(TId id)
    {
        return this.strategies.TryGetValue(id, out var strategy)
            ? strategy
            : throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                $"No {typeof(TStrategy).Name} is registered for {typeof(TId).Name}.{id}."
            );
    }
}
