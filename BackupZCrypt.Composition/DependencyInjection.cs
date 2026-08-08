using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Application.ValueObjects.Benchmark;
using BackupZCrypt.Application.ValueObjects.Manifest;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Services;
using BackupZCrypt.Infrastructure.Services.Settings;
using BackupZCrypt.Infrastructure.Strategies.Chunking;
using BackupZCrypt.Infrastructure.Strategies.Compression;
using BackupZCrypt.Infrastructure.Strategies.Encryption;
using BackupZCrypt.Infrastructure.Strategies.KeyDerivation;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Composition;

/// <summary>
/// The composition root: the single place that knows which concrete type sits behind each contract.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers every service the application is built from: the algorithm factories, the
    /// encryption / key-derivation / compression / chunking strategies, the file-system and settings
    /// adapters, and the use-case command and query handlers with the services and validators behind
    /// them.
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
    /// Every strategy is registered as a singleton against its shared interface, so consumers can
    /// resolve the whole <see cref="IEnumerable{T}"/> and index it by the strategy's enum <c>Id</c>.
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
    /// The command and query handlers are registered as closed generic interfaces, one line per
    /// message, on purpose: enumerating them keeps the composition root the single honest list of
    /// supported messages, where assembly scanning would hide it behind reflection.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection to add the registrations to.</param>
    /// <returns>The same <paramref name="services"/> instance, to allow call chaining.</returns>
    public static IServiceCollection AddBackupZCryptServices(this IServiceCollection services)
    {
        _ = services.AddSingleton<IKeyDerivationServiceFactory, KeyDerivationServiceFactory>();
        _ = services.AddSingleton<ICompressionServiceFactory, CompressionServiceFactory>();
        _ = services.AddSingleton<IEncryptionServiceFactory, EncryptionServiceFactory>();

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
        _ = services.AddSingleton<ISystemStorageService, SystemStorageService>();
        _ = services.AddSingleton<ISettingsService, SettingsService>();

        _ = services.AddSingleton<IChunkedBackupService, ChunkedBackupService>();
        _ = services.AddSingleton<IBackupBenchmarkService, BackupBenchmarkService>();
        _ = services.AddSingleton<IBackupRequestValidator, BackupRequestValidator>();
        _ = services.AddSingleton<IManifestService, ManifestService>();
        _ = services.AddSingleton<IPasswordService, PasswordService>();

        _ = services.AddSingleton<BackupOperationRunner>();

        _ = services.AddSingleton<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>, CreateBackupCommandHandler>();
        _ = services.AddSingleton<ICommandHandler<UpdateBackupCommand, Result<BackupOutcome>>, UpdateBackupCommandHandler>();
        _ = services.AddSingleton<ICommandHandler<RestoreBackupCommand, Result<BackupOutcome>>, RestoreBackupCommandHandler>();
        _ = services.AddSingleton<IQueryHandler<VerifyBackupQuery, Result<BackupOutcome>>, VerifyBackupQueryHandler>();

        _ = services.AddSingleton<IQueryHandler<DetectManifestKindQuery, ManifestKind>, DetectManifestKindQueryHandler>();
        _ = services.AddSingleton<
            ISyncQueryHandler<AnalyzePasswordStrengthQuery, PasswordStrengthAnalysis>,
            AnalyzePasswordStrengthQueryHandler
        >();
        _ = services.AddSingleton<ISyncQueryHandler<GeneratePasswordQuery, string>, GeneratePasswordQueryHandler>();
        _ = services.AddSingleton<
            IQueryHandler<EstimateBackupBenchmarkQuery, Result<BenchmarkEstimate>>,
            EstimateBackupBenchmarkQueryHandler
        >();

        _ = services.AddSingleton<
            IQueryHandler<GetSettingsQuery<BackupCreationSettings>, BackupCreationSettings>,
            GetSettingsQueryHandler<BackupCreationSettings>
        >();
        _ = services.AddSingleton<
            IQueryHandler<GetSettingsQuery<LanguageSettings>, LanguageSettings>,
            GetSettingsQueryHandler<LanguageSettings>
        >();
        _ = services.AddSingleton<
            IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings>,
            GetSettingsQueryHandler<RecentPathSettings>
        >();
        _ = services.AddSingleton<
            ICommandHandler<SaveSettingsCommand<BackupCreationSettings>, Result>,
            SaveSettingsCommandHandler<BackupCreationSettings>
        >();
        _ = services.AddSingleton<
            ICommandHandler<SaveSettingsCommand<LanguageSettings>, Result>,
            SaveSettingsCommandHandler<LanguageSettings>
        >();
        _ = services.AddSingleton<
            ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result>,
            SaveSettingsCommandHandler<RecentPathSettings>
        >();
        _ = services.AddSingleton<
            ISyncQueryHandler<GetSettingsFilePathQuery<BackupCreationSettings>, string>,
            GetSettingsFilePathQueryHandler<BackupCreationSettings>
        >();

        return services;
    }
}
