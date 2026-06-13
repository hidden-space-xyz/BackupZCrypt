using BackupZCrypt.Domain.Enums;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

/// <summary>
/// AEAD encryption strategy using the Camellia block cipher in GCM mode via BouncyCastle.
/// </summary>
internal sealed class CamelliaEncryptionStrategy : BouncyCastleGcmEncryptionStrategyBase
{
    /// <summary>
    /// Gets the algorithm identifier (<see cref="EncryptionAlgorithm.Camellia"/>) used to select this strategy.
    /// </summary>
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Camellia;

    /// <summary>
    /// Creates a GCM-mode AEAD cipher wrapping a Camellia engine.
    /// </summary>
    /// <returns>A new Camellia-GCM cipher instance.</returns>
    protected override IAeadCipher CreateCipher()
    {
        return new GcmBlockCipher(new CamelliaEngine());
    }
}
