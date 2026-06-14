using BackupZCrypt.Domain.Enums;

using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

/// <summary>
/// AEAD encryption strategy using the Twofish block cipher in GCM mode via BouncyCastle.
/// </summary>
internal sealed class TwofishEncryptionStrategy : BouncyCastleGcmEncryptionStrategyBase
{
    /// <summary>
    /// Gets the algorithm identifier (<see cref="EncryptionAlgorithm.Twofish"/>) used to select this strategy.
    /// </summary>
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Twofish;

    /// <summary>
    /// Creates a GCM-mode AEAD cipher wrapping a Twofish engine.
    /// </summary>
    /// <returns>A new Twofish-GCM cipher instance.</returns>
    protected override IAeadCipher CreateCipher()
    {
        return new GcmBlockCipher(new TwofishEngine());
    }
}
