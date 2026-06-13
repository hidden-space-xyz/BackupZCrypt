using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Factories.Interfaces;

/// <summary>
/// Resolves the compression strategy that implements a given compression mode.
/// </summary>
public interface ICompressionServiceFactory
{
    /// <summary>
    /// Returns the strategy registered for the specified compression mode.
    /// </summary>
    /// <param name="mode">The compression mode to resolve.</param>
    /// <returns>The matching <see cref="ICompressionStrategy"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No strategy is registered for <paramref name="mode"/>.</exception>
    ICompressionStrategy Create(CompressionMode mode);
}
