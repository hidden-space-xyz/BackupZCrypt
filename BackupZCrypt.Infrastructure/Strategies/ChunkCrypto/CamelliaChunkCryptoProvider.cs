namespace BackupZCrypt.Infrastructure.Strategies.ChunkCrypto;

using BackupZCrypt.Domain.Enums;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;

internal sealed class CamelliaChunkCryptoProvider : BouncyCastleChunkCryptoProviderBase
{
    public override EncryptionAlgorithm Id => EncryptionAlgorithm.Camellia;

    protected override IAeadCipher CreateCipher()
    {
        return new GcmBlockCipher(new CamelliaEngine());
    }
}
