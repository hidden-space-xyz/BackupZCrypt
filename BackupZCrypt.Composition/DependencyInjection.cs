using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Benchmark;
using BackupZCrypt.Application.Engine;
using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Application.Manifest;
using BackupZCrypt.Application.Passwords;
using BackupZCrypt.Application.Settings;
using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Infrastructure.Algorithms.Chunking;
using BackupZCrypt.Infrastructure.Algorithms.Compression;
using BackupZCrypt.Infrastructure.Algorithms.Encryption;
using BackupZCrypt.Infrastructure.Algorithms.KeyDerivation;
using BackupZCrypt.Infrastructure.FileSystem;
using BackupZCrypt.Infrastructure.Settings;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Composition;

/// <summary>
/// The composition root: the single place that knows which concrete type sits behind each contract.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers every service the application is built from: the strategy registry, the
    /// encryption / key-derivation / compression / chunking strategies, the file-system and settings
    /// adapters, the backup engine, and the use-case services a front end calls with the validators
    /// behind them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately one method rather than one per layer. The registrations were previously
    /// split as <c>AddDomainServices</c> and <c>AddApplicationServices</c>, but the split did not
    /// follow the layers — the first also registered thirteen Infrastructure strategies and one
    /// Application service — and both were always called together from the only two call sites.
    /// One honestly named method is simpler than two that misdescribe themselves.
    /// </para>
    /// <para>
    /// Every strategy is registered as a singleton against its shared interface, so the open generic
    /// <see cref="IStrategyRegistry{TId, TStrategy}"/> can resolve the whole <see cref="IEnumerable{T}"/>
    /// of a family and index it by the strategy's enum <c>Id</c>.
    /// The Desktop-only platform services and the ViewModels are registered in
    /// <c>App.ConfigureServices</c> instead: this project must not reference Avalonia, since that
    /// would point a dependency arrow outward.
    /// </para>
    /// <para>
    /// Chunking is the one family registered exactly once, by design. The manifest records no chunker
    /// identifier, so a second implementation would change chunk boundaries with nothing on disk to
    /// say which one produced them, destroying deduplication against every archive already written.
    /// </para>
    /// <para>
    /// A front end depends only on the use-case services: <see cref="IBackupOperations"/>,
    /// <see cref="ISettingsStore"/>, <see cref="IPasswordService"/>,
    /// <see cref="IBackupBenchmarkService"/>, and <see cref="IAlgorithmCatalog"/>. Every other
    /// registration is a collaborator of these.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <returns>The same <paramref name="services"/> instance, to allow call chaining.</returns>
    public static IServiceCollection AddBackupZCryptServices(this IServiceCollection services)
    {
        _ = services.AddSingleton(typeof(IStrategyRegistry<,>), typeof(StrategyRegistry<,>));

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

        _ = services.AddSingleton<IFileOperationsService, FileOperationsService>();
        _ = services.AddSingleton<IDirectoryScanService, DirectoryScanService>();
        _ = services.AddSingleton<IFileMetadataService, FileMetadataService>();
        _ = services.AddSingleton<IFileLockService, FileLockService>();
        _ = services.AddSingleton<ISystemStorageService, SystemStorageService>();
        _ = services.AddSingleton<ISettingsService, SettingsService>();

        _ = services.AddSingleton<ChunkStore>();
        _ = services.AddSingleton<BackupOpener>();
        _ = services.AddSingleton<BackupWriter>();
        _ = services.AddSingleton<BackupRestorer>();
        _ = services.AddSingleton<BackupPreviewer>();
        _ = services.AddSingleton<IChunkedBackupService, ChunkedBackupService>();
        _ = services.AddSingleton<IBackupBenchmarkService, BackupBenchmarkService>();
        _ = services.AddSingleton<IBackupRequestValidator, BackupRequestValidator>();
        _ = services.AddSingleton<IManifestService, ManifestService>();
        _ = services.AddSingleton<IPasswordService, PasswordService>();

        _ = services.AddSingleton<IAlgorithmCatalog, AlgorithmCatalog>();
        _ = services.AddSingleton<ISettingsStore, SettingsStore>();
        _ = services.AddSingleton<IBackupOperations, BackupOperations>();

        return services;
    }
}
