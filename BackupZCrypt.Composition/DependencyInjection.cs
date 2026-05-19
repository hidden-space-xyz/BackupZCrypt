namespace BackupZCrypt.Composition;

using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Services.FileSystem;
using BackupZCrypt.Infrastructure.Strategies.ChunkCrypto;
using BackupZCrypt.Infrastructure.Strategies.Chunking;
using BackupZCrypt.Infrastructure.Strategies.Compression;
using BackupZCrypt.Infrastructure.Strategies.Encryption.Algorithms;
using BackupZCrypt.Infrastructure.Strategies.KeyDerivation;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        // Factories
        services.AddSingleton<IKeyDerivationServiceFactory, KeyDerivationServiceFactory>();
        services.AddSingleton<ICompressionServiceFactory, CompressionServiceFactory>();
        services.AddSingleton<IChunkCryptoProviderFactory, ChunkCryptoProviderFactory>();

        // Key Derivation Strategies
        services.AddSingleton<IKeyDerivationAlgorithmStrategy, Argon2IdKeyDerivationStrategy>();
        services.AddSingleton<IKeyDerivationAlgorithmStrategy, Pbkdf2KeyDerivationStrategy>();
        services.AddSingleton<IKeyDerivationAlgorithmStrategy, ScryptKeyDerivationStrategy>();

        // Encryption Strategies
        services.AddSingleton<IEncryptionAlgorithmStrategy, AesEncryptionStrategy>();
        services.AddSingleton<IEncryptionAlgorithmStrategy, TwofishEncryptionStrategy>();
        services.AddSingleton<IEncryptionAlgorithmStrategy, SerpentEncryptionStrategy>();
        services.AddSingleton<IEncryptionAlgorithmStrategy, ChaCha20EncryptionStrategy>();
        services.AddSingleton<IEncryptionAlgorithmStrategy, CamelliaEncryptionStrategy>();

        // Chunk Crypto Providers
        services.AddSingleton<IChunkCryptoProvider, AesChunkCryptoProvider>();
        services.AddSingleton<IChunkCryptoProvider, ChaCha20ChunkCryptoProvider>();
        services.AddSingleton<IChunkCryptoProvider, TwofishChunkCryptoProvider>();
        services.AddSingleton<IChunkCryptoProvider, SerpentChunkCryptoProvider>();
        services.AddSingleton<IChunkCryptoProvider, CamelliaChunkCryptoProvider>();

        // Compression Strategies
        services.AddSingleton<ICompressionStrategy, ZstdFastCompressionStrategy>();
        services.AddSingleton<ICompressionStrategy, ZstdCompressionStrategy>();
        services.AddSingleton<ICompressionStrategy, ZstdBestCompressionStrategy>();

        // Content Chunking
        services.AddSingleton<IContentChunker, FastCdcChunker>();

        // Services
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IFileOperationsService, FileOperationsService>();
        services.AddSingleton<ISystemStorageService, SystemStorageService>();

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IBackupOrchestrator, BackupOrchestrator>();
        services.AddSingleton<IChunkedBackupService, ChunkedBackupService>();
        services.AddSingleton<IFileBackupService, FileBackupService>();
        services.AddSingleton<IDirectoryBackupService, DirectoryBackupService>();
        services.AddSingleton<IBackupRequestValidator, BackupRequestValidator>();
        services.AddSingleton<IManifestService, ManifestService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        return services;
    }
}
