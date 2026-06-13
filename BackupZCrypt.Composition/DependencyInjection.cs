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
using BackupZCrypt.Infrastructure.Services;
using BackupZCrypt.Infrastructure.Strategies.Chunking;
using BackupZCrypt.Infrastructure.Strategies.Compression;
using BackupZCrypt.Infrastructure.Strategies.Encryption;
using BackupZCrypt.Infrastructure.Strategies.KeyDerivation;
using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Composition;

/// <summary>
/// Composition-root extension methods that wire Domain contracts to their Infrastructure
/// implementations and register the Application-layer services in the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers the Infrastructure implementations of Domain contracts: the factories,
    /// the key-derivation/encryption/compression/chunking algorithm strategies, and the
    /// file-system and storage services. Every strategy is registered as a singleton so
    /// consumers can resolve the full <see cref="IEnumerable{T}"/> set and index by the
    /// strategy's enum <c>Id</c>.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <returns>The same <paramref name="services"/> instance, to allow call chaining.</returns>
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddSingleton<IKeyDerivationServiceFactory, KeyDerivationServiceFactory>();
        services.AddSingleton<ICompressionServiceFactory, CompressionServiceFactory>();

        services.AddSingleton<IKeyDerivationAlgorithmStrategy, Argon2IdKeyDerivationStrategy>();
        services.AddSingleton<IKeyDerivationAlgorithmStrategy, Pbkdf2KeyDerivationStrategy>();
        services.AddSingleton<IKeyDerivationAlgorithmStrategy, ScryptKeyDerivationStrategy>();

        services.AddSingleton<IEncryptionAlgorithmStrategy, NoneEncryptionStrategy>();
        services.AddSingleton<IEncryptionAlgorithmStrategy, AesEncryptionStrategy>();
        services.AddSingleton<IEncryptionAlgorithmStrategy, TwofishEncryptionStrategy>();
        services.AddSingleton<IEncryptionAlgorithmStrategy, SerpentEncryptionStrategy>();
        services.AddSingleton<IEncryptionAlgorithmStrategy, ChaCha20EncryptionStrategy>();
        services.AddSingleton<IEncryptionAlgorithmStrategy, CamelliaEncryptionStrategy>();

        services.AddSingleton<ICompressionStrategy, ZstdFastCompressionStrategy>();
        services.AddSingleton<ICompressionStrategy, ZstdCompressionStrategy>();
        services.AddSingleton<ICompressionStrategy, ZstdBestCompressionStrategy>();

        services.AddSingleton<IChunkingStrategy, FastCdcChunkingStrategy>();

        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IFileOperationsService, FileOperationsService>();
        services.AddSingleton<ISystemStorageService, SystemStorageService>();

        return services;
    }

    /// <summary>
    /// Registers the Application-layer orchestrators, backup/manifest/settings services,
    /// and request validators as singletons.
    /// </summary>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <returns>The same <paramref name="services"/> instance, to allow call chaining.</returns>
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
