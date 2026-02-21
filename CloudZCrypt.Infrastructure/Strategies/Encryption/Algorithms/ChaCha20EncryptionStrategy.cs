namespace CloudZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

internal class ChaCha20EncryptionStrategy(
    IEncryptionSessionFactory encryptionSessionFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IEncryptionFileService encryptionFileService)
    : EncryptionStrategyBase(
        encryptionSessionFactory,
        compressionServiceFactory,
        encryptionFileService),
        IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.ChaCha20;

    public string DisplayName => Messages.ChaCha20DisplayName;

    public string Description => Messages.ChaCha20Description;

    public string Summary => Messages.ChaCha20Summary;

    protected override async Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken)
    {
        ChaCha20Poly1305 chacha20Poly1305 = new();
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        chacha20Poly1305.Init(true, parameters);

        await ProcessFileWithCipherAsync(
            sourceStream,
            destinationStream,
            chacha20Poly1305,
            cancellationToken);
    }

    protected override async Task DecryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken)
    {
        ChaCha20Poly1305 chacha20Poly1305 = new();
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        chacha20Poly1305.Init(false, parameters);

        await ProcessFileWithCipherAsync(
            sourceStream,
            destinationStream,
            chacha20Poly1305,
            cancellationToken);
    }
}
