using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace CloudZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

internal class ChaCha20EncryptionStrategy(
    IKeyDerivationServiceFactory keyDerivationServiceFactory,
    ICompressionServiceFactory compressionServiceFactory
)
    : EncryptionStrategyBase(keyDerivationServiceFactory, compressionServiceFactory),
        IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.ChaCha20;

    public string DisplayName => "ChaCha20-Poly1305";

    public string Description =>
        "A modern ARX (Add-Rotate-XOR) stream cipher (ChaCha20) combined with the Poly1305 MAC to form a fast, "
        + "timing‑attack‑resistant AEAD construction that performs especially well on devices lacking AES hardware acceleration. "
        + "Standardized in RFC 8439 and widely deployed in TLS, SSH, QUIC, and WireGuard.";

    public string Summary => "Best for general purposes (without hardware acceleration)";

    protected override async Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce
    )
    {
        ChaCha20Poly1305 chacha20Poly1305 = new();
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce);
        chacha20Poly1305.Init(true, parameters);

        await ProcessFileWithCipherAsync(sourceStream, destinationStream, chacha20Poly1305);
    }

    protected override async Task DecryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce
    )
    {
        ChaCha20Poly1305 chacha20Poly1305 = new();
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce);
        chacha20Poly1305.Init(false, parameters);

        await ProcessFileWithCipherAsync(sourceStream, destinationStream, chacha20Poly1305);
    }
}
