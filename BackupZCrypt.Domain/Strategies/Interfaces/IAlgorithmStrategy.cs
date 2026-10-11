namespace BackupZCrypt.Domain.Strategies.Interfaces;

/// <summary>
/// Identifies a strategy by the enum value of the algorithm or mode it implements, which is what an
/// archive records and what the application looks it up by.
/// </summary>
/// <typeparam name="TId">The enum whose members name the implementations of this strategy family.</typeparam>
public interface IAlgorithmStrategy<TId>
    where TId : struct, Enum
{
    /// <summary>
    /// Gets the algorithm or mode this strategy implements, used to select it by enum value.
    /// </summary>
    public TId Id { get; }
}
