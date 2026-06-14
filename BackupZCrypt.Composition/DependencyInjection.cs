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
        _ = services.AddSingleton<IKeyDerivationServiceFactory, KeyDerivationServiceFactory>();
        _ = services.AddSingleton<ICompressionServiceFactory, CompressionServiceFactory>();

        _ = services.AddSingleton<IKeyDerivationAlgorithmStrategy, Argon2IdKeyDerivationStrategy>();
        _ = services.AddSingleton<IKeyDerivationAlgorithmStrategy, Pbkdf2KeyDerivationStrategy>();
        _ = services.AddSingleton<IKeyDerivationAlgorithmStrategy, ScryptKeyDerivationStrategy>();

        _ = services.AddSingleton<IEncryptionAlgorithmStrategy, AesEncryptionStrategy>();
        _ = services.AddSingleton<IEncryptionAlgorithmStrategy, TwofishEncryptionStrategy>();
        _ = services.AddSingleton<IEncryptionAlgorithmStrategy, SerpentEncryptionStrategy>();
        _ = services.AddSingleton<IEncryptionAlgorithmStrategy, ChaCha20EncryptionStrategy>();
        _ = services.AddSingleton<IEncryptionAlgorithmStrategy, CamelliaEncryptionStrategy>();

        _ = services.AddSingleton<ICompressionStrategy, ZstdFastCompressionStrategy>();
        _ = services.AddSingleton<ICompressionStrategy, ZstdCompressionStrategy>();
        _ = services.AddSingleton<ICompressionStrategy, ZstdBestCompressionStrategy>();

        _ = services.AddSingleton<IChunkingStrategy, FastCdcChunkingStrategy>();

        _ = services.AddSingleton<IPasswordService, PasswordService>();
        _ = services.AddSingleton<IFileOperationsService, FileOperationsService>();
        _ = services.AddSingleton<ISystemStorageService, SystemStorageService>();

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
        _ = services.AddSingleton<IBackupOrchestrator, BackupOrchestrator>();
        _ = services.AddSingleton<IChunkedBackupService, ChunkedBackupService>();
        _ = services.AddSingleton<IBackupBenchmarkService, BackupBenchmarkService>();
        _ = services.AddSingleton<IBackupRequestValidator, BackupRequestValidator>();
        _ = services.AddSingleton<IManifestService, ManifestService>();
        _ = services.AddSingleton<ISettingsService, SettingsService>();

        return services;
    }
}
