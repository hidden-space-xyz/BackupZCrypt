using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Domain.Algorithms;

using NSubstitute;

namespace BackupZCrypt.Test.Common;

/// <summary>
/// Builds substituted <see cref="IAlgorithmCatalog"/> instances for tests that need to control which
/// algorithms are offered and in which order.
/// </summary>
internal static class AlgorithmCatalogs
{
    /// <summary>
    /// Creates a catalog offering every member of each algorithm enum, in identifier order.
    /// </summary>
    /// <returns>The substituted catalog.</returns>
    internal static IAlgorithmCatalog Every()
    {
        return Offering(
            Enum.GetValues<EncryptionAlgorithm>(),
            Enum.GetValues<KeyDerivationAlgorithm>(),
            Enum.GetValues<CompressionMode>()
        );
    }

    /// <summary>
    /// Creates a catalog offering exactly the given algorithms, in the given order.
    /// </summary>
    /// <param name="encryption">The ciphers offered.</param>
    /// <param name="keyDerivation">The key derivation functions offered.</param>
    /// <param name="compression">The compression modes offered.</param>
    /// <returns>The substituted catalog.</returns>
    internal static IAlgorithmCatalog Offering(
        EncryptionAlgorithm[] encryption,
        KeyDerivationAlgorithm[] keyDerivation,
        CompressionMode[] compression
    )
    {
        var catalog = Substitute.For<IAlgorithmCatalog>();
        _ = catalog.EncryptionAlgorithms.Returns(encryption);
        _ = catalog.KeyDerivationAlgorithms.Returns(keyDerivation);
        _ = catalog.CompressionModes.Returns(compression);

        return catalog;
    }
}
