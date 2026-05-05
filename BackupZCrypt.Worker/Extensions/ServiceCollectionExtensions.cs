namespace BackupZCrypt.Worker.Extensions;

using BackupZCrypt.Domain.Enums;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<WorkerConfiguration>(config =>
        {
            config.BackupSourcePath = configuration["BACKUP_SOURCE_PATH"] ?? config.BackupSourcePath;
            config.BackupDestinationPath = configuration["BACKUP_DESTINATION_PATH"] ?? config.BackupDestinationPath;
            config.RestoreSourcePath = configuration["RESTORE_SOURCE_PATH"] ?? config.RestoreSourcePath;
            config.RestoreDestinationPath = configuration["RESTORE_DESTINATION_PATH"] ?? config.RestoreDestinationPath;
            config.Password = configuration["BACKUP_PASSWORD"] ?? config.Password;

            BindBool(configuration, "BACKUP_USE_ENCRYPTION", v => config.UseEncryption = v);
            BindBool(configuration, "BACKUP_DELETE_SOURCE_FILES", v => config.DeleteSourceFiles = v);

            BindEnum<EncryptionAlgorithm>(configuration, "BACKUP_ENCRYPTION_ALGORITHM", v => config.EncryptionAlgorithm = v);
            BindEnum<KeyDerivationAlgorithm>(configuration, "BACKUP_KEY_DERIVATION_ALGORITHM", v => config.KeyDerivationAlgorithm = v);
            BindEnum<NameObfuscationMode>(configuration, "BACKUP_NAME_OBFUSCATION", v => config.NameObfuscation = v);
            BindEnum<CompressionMode>(configuration, "BACKUP_COMPRESSION", v => config.Compression = v);
        });

        return services;
    }

    private static void BindBool(
        IConfiguration configuration,
        string key,
        Action<bool> setter)
    {
        if (bool.TryParse(configuration[key], out var value))
        {
            setter(value);
        }
    }

    private static void BindEnum<T>(
        IConfiguration configuration,
        string key,
        Action<T> setter) where T : struct, Enum
    {
        if (Enum.TryParse<T>(configuration[key], ignoreCase: true, out var value))
        {
            setter(value);
        }
    }
}
