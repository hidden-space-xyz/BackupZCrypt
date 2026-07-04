using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Factories.Interfaces;

/// <summary>
/// Resolves the encryption strategy that implements a given algorithm.
/// </summary>
public interface IEncryptionServiceFactory
{
    /// <summary>
    /// Returns the strategy registered for the specified encryption algorithm.
    /// </summary>
    /// <param name="algorithm">The encryption algorithm to resolve.</param>
    /// <returns>The matching <see cref="IEncryptionAlgorithmStrategy"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No strategy is registered for <paramref name="algorithm"/>.</exception>
    public IEncryptionAlgorithmStrategy Create(EncryptionAlgorithm algorithm);
}
