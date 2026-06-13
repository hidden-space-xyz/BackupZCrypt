using BackupZCrypt.Domain.Enums;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

/// <summary>
/// AEAD encryption strategy using the Serpent block cipher in GCM mode via BouncyCastle.
/// </summary>
internal sealed class SerpentEncryptionStrategy : BouncyCastleGcmEncryptionStrategyBase
{
    /// <summary>
    /// Gets the algorithm identifier (<see cref="EncryptionAlgorithm.Serpent"/>) used to select this strategy.
    /// </summary>
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Serpent;

    /// <summary>
    /// Creates a GCM-mode AEAD cipher wrapping a Serpent engine.
    /// </summary>
    /// <returns>A new Serpent-GCM cipher instance.</returns>
    protected override IAeadCipher CreateCipher()
    {
        return new GcmBlockCipher(new SerpentEngine());
    }
}
