namespace CloudZCrypt.Infrastructure.Strategies.Encryption.Algorithms;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

internal class SerpentEncryptionStrategy(
    IEncryptionSessionFactory encryptionSessionFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IEncryptionFileService encryptionFileService)
    : EncryptionStrategyBase(
        encryptionSessionFactory,
        compressionServiceFactory,
        encryptionFileService),
        IEncryptionAlgorithmStrategy
{
    public EncryptionAlgorithm Id => EncryptionAlgorithm.Serpent;

    public string DisplayName => Messages.SerpentDisplayName;

    public string Description => Messages.SerpentDescription;

    public string Summary => Messages.SerpentSummary;

    protected override async Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken)
    {
        SerpentEngine serpentEngine = new();
        GcmBlockCipher gcmCipher = new(serpentEngine);
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        gcmCipher.Init(true, parameters);

        await this.ProcessFileWithCipherAsync(
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
        SerpentEngine serpentEngine = new();
        GcmBlockCipher gcmCipher = new(serpentEngine);
        AeadParameters parameters = new(new KeyParameter(key), MacSize, nonce, associatedData);
        gcmCipher.Init(false, parameters);

        await this.ProcessFileWithCipherAsync(
            sourceStream,
            destinationStream,
            gcmCipher,
            cancellationToken);
    }
}
