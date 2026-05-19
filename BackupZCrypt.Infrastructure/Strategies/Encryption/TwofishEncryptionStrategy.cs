namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

internal sealed class TwofishEncryptionStrategy : BouncyCastleGcmEncryptionStrategyBase
{
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Twofish;

    public override string DisplayName => Messages.TwofishDisplayName;

    public override string Description => Messages.TwofishDescription;

    public override string Summary => Messages.TwofishSummary;

    protected override IAeadCipher CreateCipher()
    {
        return new GcmBlockCipher(new TwofishEngine());
    }
}
