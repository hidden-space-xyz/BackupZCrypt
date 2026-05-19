namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

internal sealed class SerpentEncryptionStrategy : BouncyCastleGcmEncryptionStrategyBase
{
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Serpent;

    public override string DisplayName => Messages.SerpentDisplayName;

    public override string Description => Messages.SerpentDescription;

    public override string Summary => Messages.SerpentSummary;

    protected override IAeadCipher CreateCipher()
    {
        return new GcmBlockCipher(new SerpentEngine());
    }
}
