using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

internal sealed class CamelliaEncryptionStrategy : BouncyCastleGcmEncryptionStrategyBase
{
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Camellia;

    public override string DisplayName => Messages.CamelliaDisplayName;

    public override string Description => Messages.CamelliaDescription;

    public override string Summary => Messages.CamelliaSummary;

    protected override IAeadCipher CreateCipher()
    {
        return new GcmBlockCipher(new CamelliaEngine());
    }
}
