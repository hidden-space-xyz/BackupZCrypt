using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace CloudZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

internal class AesEncryptionStrategy(
    IKeyDerivationServiceFactory keyDerivationServiceFactory,
    ICompressionServiceFactory compressionServiceFactory
)
    : EncryptionStrategyBase(keyDerivationServiceFactory, compressionServiceFactory),
        IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Aes;

    public string DisplayName => "AES-256 GCM";

    public string Description =>
        "A NIST-standardized 128‑bit block cipher with a 256‑bit key, widely accelerated via AES-NI and ARMv8 Cryptography Extensions. "
        + "Galois/Counter Mode (GCM) provides authenticated encryption with associated data (AEAD), combining high performance, confidentiality, and integrity.";

    public string Summary => "Best for general purposes (with hardware acceleration)";

    protected override async Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce
    )
    {
        AesEngine aesEngine = new();
        GcmBlockCipher gcmCipher = new(aesEngine);
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce);
        gcmCipher.Init(true, parameters);

        await ProcessFileWithCipherAsync(sourceStream, destinationStream, gcmCipher);
    }

    protected override async Task DecryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce
    )
    {
        AesEngine aesEngine = new();
        GcmBlockCipher gcmCipher = new(aesEngine);
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce);
        gcmCipher.Init(false, parameters);

        await ProcessFileWithCipherAsync(sourceStream, destinationStream, gcmCipher);
    }
}
