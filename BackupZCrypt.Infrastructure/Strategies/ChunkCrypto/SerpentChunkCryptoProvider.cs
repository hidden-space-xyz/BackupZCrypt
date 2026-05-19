namespace BackupZCrypt.Infrastructure.Strategies.ChunkCrypto;

using BackupZCrypt.Domain.Enums;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

internal sealed class SerpentChunkCryptoProvider : BouncyCastleChunkCryptoProviderBase
{
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Serpent;

    protected override IAeadCipher CreateCipher()
    {
        return new GcmBlockCipher(new SerpentEngine());
    }
}
