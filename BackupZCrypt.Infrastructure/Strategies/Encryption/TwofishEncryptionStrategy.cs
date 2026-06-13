using BackupZCrypt.Domain.Enums;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

internal sealed class TwofishEncryptionStrategy : BouncyCastleGcmEncryptionStrategyBase
{
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Twofish;

    protected override IAeadCipher CreateCipher()
    {
        return new GcmBlockCipher(new TwofishEngine());
    }
}
