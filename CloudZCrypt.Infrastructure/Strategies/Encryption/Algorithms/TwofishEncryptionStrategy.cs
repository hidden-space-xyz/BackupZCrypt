namespace CloudZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

internal class TwofishEncryptionStrategy(
    IEncryptionSessionFactory encryptionSessionFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IEncryptionFileService encryptionFileService)
    : EncryptionStrategyBase(
        encryptionSessionFactory,
        compressionServiceFactory,
        encryptionFileService),
        IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Twofish;

    public string DisplayName => Messages.TwofishDisplayName;

    public string Description => Messages.TwofishDescription;

    public string Summary => Messages.TwofishSummary;

    protected override async Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken)
    {
        TwofishEngine twofishEngine = new();
        GcmBlockCipher gcmCipher = new(twofishEngine);
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        gcmCipher.Init(true, parameters);

        await ProcessFileWithCipherAsync(
            sourceStream,
            destinationStream,
            gcmCipher,
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
        TwofishEngine twofishEngine = new();
        GcmBlockCipher gcmCipher = new(twofishEngine);
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        gcmCipher.Init(false, parameters);

        await ProcessFileWithCipherAsync(
            sourceStream,
            destinationStream,
            gcmCipher,
            cancellationToken);
    }
}
