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
using BackupZCrypt.Infrastructure.Services.Encryption;
using BackupZCrypt.Infrastructure.Services.FileSystem;
using BackupZCrypt.Infrastructure.Strategies.Compression;
using BackupZCrypt.Infrastructure.Strategies.Encryption.Algorithms;
using BackupZCrypt.Infrastructure.Strategies.KeyDerivation;
using BackupZCrypt.Infrastructure.Strategies.Obfuscation;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        // Factories
        services.AddSingleton<IKeyDerivationServiceFactory, KeyDerivationServiceFactory>();
        services.AddSingleton<IEncryptionServiceFactory, EncryptionServiceFactory>();
        services.AddSingleton<INameObfuscationServiceFactory, NameObfuscationServiceFactory>();
        services.AddSingleton<ICompressionServiceFactory, CompressionServiceFactory>();

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

        // Compression Strategies
        services.AddSingleton<ICompressionStrategy, ZstdFastCompressionStrategy>();
        services.AddSingleton<ICompressionStrategy, ZstdCompressionStrategy>();
        services.AddSingleton<ICompressionStrategy, ZstdBestCompressionStrategy>();

        // Name Obfuscation Strategies
        services.AddSingleton<INameObfuscationStrategy, GuidObfuscationStrategy>();
        services.AddSingleton<INameObfuscationStrategy, Sha256ObfuscationStrategy>();
        services.AddSingleton<INameObfuscationStrategy, Sha512ObfuscationStrategy>();

        // Services
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddSingleton<IFileOperationsService, FileOperationsService>();
        services.AddSingleton<ISystemStorageService, SystemStorageService>();
        services.AddSingleton<IEncryptionSessionFactory, EncryptionSessionFactory>();
        services.AddSingleton<IEncryptionFileService, EncryptionFileService>();

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IBackupOrchestrator, BackupOrchestrator>();
        services.AddSingleton<ISingleFileBackupService, SingleFileBackupService>();
        services.AddSingleton<IDirectoryBackupService, DirectoryBackupService>();
        services.AddSingleton<IBackupRequestValidator, BackupRequestValidator>();
        services.AddSingleton<IManifestService, ManifestService>();

        return services;
    }
}
