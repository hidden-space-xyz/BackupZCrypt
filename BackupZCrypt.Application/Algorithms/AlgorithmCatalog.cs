using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Algorithms;

/// <summary>
/// Lists the algorithms the strategy registries hold, adding <see cref="CompressionMode.None"/>,
/// which needs no strategy, in front of the compression modes.
/// </summary>
/// <remarks>
/// Built from the same registries the engine resolves strategies through, so an algorithm is offered
/// exactly when the engine can write it.
/// </remarks>
internal sealed class AlgorithmCatalog : IAlgorithmCatalog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AlgorithmCatalog"/> class.
    /// </summary>
    /// <param name="encryptionStrategies">The registered encryption strategies.</param>
    /// <param name="keyDerivationStrategies">The registered key derivation strategies.</param>
    /// <param name="compressionStrategies">The registered compression strategies.</param>
    public AlgorithmCatalog(
        IStrategyRegistry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> encryptionStrategies,
        IStrategyRegistry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
        IStrategyRegistry<CompressionMode, ICompressionStrategy> compressionStrategies
    )
    {
        ArgumentNullException.ThrowIfNull(encryptionStrategies);
        ArgumentNullException.ThrowIfNull(keyDerivationStrategies);
        ArgumentNullException.ThrowIfNull(compressionStrategies);

        this.EncryptionAlgorithms = encryptionStrategies.Ids;
        this.KeyDerivationAlgorithms = keyDerivationStrategies.Ids;
        this.CompressionModes = Array.AsReadOnly(
            [CompressionMode.None, .. compressionStrategies.Ids.Where(static mode => mode is not CompressionMode.None)]
        );
    }

    /// <inheritdoc/>
    public IReadOnlyList<EncryptionAlgorithm> EncryptionAlgorithms { get; }

    /// <inheritdoc/>
    public IReadOnlyList<KeyDerivationAlgorithm> KeyDerivationAlgorithms { get; }

    /// <inheritdoc/>
    public IReadOnlyList<CompressionMode> CompressionModes { get; }
}
