using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace CloudZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

internal class CamelliaEncryptionStrategy(
    IKeyDerivationServiceFactory keyDerivationServiceFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IFileOperationsService fileOperationsService,
    ISystemStorageService systemStorageService
)
    : EncryptionStrategyBase(keyDerivationServiceFactory, compressionServiceFactory, fileOperationsService, systemStorageService),
        IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Camellia;

    public string DisplayName => Messages.CamelliaDisplayName;

    public string Description => Messages.CamelliaDescription;

    public string Summary => Messages.CamelliaSummary;

    protected override async Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken
    )
    {
        CamelliaEngine camelliaEngine = new();
        GcmBlockCipher gcmCipher = new(camelliaEngine);
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
        CamelliaEngine camelliaEngine = new();
        GcmBlockCipher gcmCipher = new(camelliaEngine);
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        gcmCipher.Init(false, parameters);

        await ProcessFileWithCipherAsync(sourceStream, destinationStream, gcmCipher, cancellationToken);
    }
}
