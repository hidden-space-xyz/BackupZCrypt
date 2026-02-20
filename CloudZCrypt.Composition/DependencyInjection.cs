namespace CloudZCrypt.Composition;

using CloudZCrypt.Application.Orchestrators;
using CloudZCrypt.Application.Orchestrators.Interfaces;
using CloudZCrypt.Application.Services;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.Validators;
using CloudZCrypt.Application.Validators.Interfaces;
using CloudZCrypt.Domain.Factories;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Services.Encryption;
using CloudZCrypt.Infrastructure.Services.FileSystem;
using CloudZCrypt.Infrastructure.Strategies.Compression;
using CloudZCrypt.Infrastructure.Strategies.Encryption.Algorithms;
using CloudZCrypt.Infrastructure.Strategies.KeyDerivation;
using CloudZCrypt.Infrastructure.Strategies.Obfuscation;
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
        services.AddSingleton<ICompressionStrategy, NoCompressionStrategy>();
        services.AddSingleton<ICompressionStrategy, GZipCompressionStrategy>();
        services.AddSingleton<ICompressionStrategy, BZip2CompressionStrategy>();
        services.AddSingleton<ICompressionStrategy, LzmaCompressionStrategy>();

        // Name Obfuscation Strategies
        services.AddSingleton<INameObfuscationStrategy, NoObfuscationStrategy>();
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
        services.AddSingleton<IFileCryptOrchestrator, FileCryptOrchestrator>();
        services.AddSingleton<IFileCryptSingleFileService, FileCryptSingleFileService>();
        services.AddSingleton<IFileCryptDirectoryService, FileCryptDirectoryService>();
        services.AddSingleton<IFileCryptRequestValidator, FileCryptRequestValidator>();
        services.AddSingleton<IManifestService, ManifestService>();

        return services;
    }
}
