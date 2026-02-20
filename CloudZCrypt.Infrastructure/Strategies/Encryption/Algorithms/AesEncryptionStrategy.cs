using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;
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

    public string DisplayName => Messages.AesDisplayName;

    public string Description => Messages.AesDescription;

    public string Summary => Messages.AesSummary;

    protected override async Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken
    )
    {
        AesEngine aesEngine = new();
        GcmBlockCipher gcmCipher = new(aesEngine);
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        gcmCipher.Init(true, parameters);

        await ProcessFileWithCipherAsync(sourceStream, destinationStream, gcmCipher, cancellationToken);
    }

    protected override async Task DecryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken
    )
    {
        AesEngine aesEngine = new();
        GcmBlockCipher gcmCipher = new(aesEngine);
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        gcmCipher.Init(false, parameters);

        await ProcessFileWithCipherAsync(sourceStream, destinationStream, gcmCipher, cancellationToken);
    }
}
