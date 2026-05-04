using BackupZCrypt.Composition;
using BackupZCrypt.Worker;

using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDomainServices();
builder.Services.AddApplicationServices();

builder.Services.Configure<WorkerConfiguration>(config =>
{
    var env = builder.Configuration;
    config.BackupSourcePath = env["BACKUP_SOURCE_PATH"] ?? config.BackupSourcePath;
    config.BackupDestinationPath = env["BACKUP_DESTINATION_PATH"] ?? config.BackupDestinationPath;
    config.RestoreSourcePath = env["RESTORE_SOURCE_PATH"] ?? config.RestoreSourcePath;
    config.RestoreDestinationPath = env["RESTORE_DESTINATION_PATH"] ?? config.RestoreDestinationPath;
    config.Password = env["BACKUP_PASSWORD"] ?? config.Password;

    if (bool.TryParse(env["BACKUP_USE_ENCRYPTION"], out var useEnc))
    {
        config.UseEncryption = useEnc;
    }

    if (Enum.TryParse<BackupZCrypt.Domain.Enums.EncryptionAlgorithm>(
        env["BACKUP_ENCRYPTION_ALGORITHM"], ignoreCase: true, out var encAlg))
    {
        config.EncryptionAlgorithm = encAlg;
    }

    if (Enum.TryParse<BackupZCrypt.Domain.Enums.KeyDerivationAlgorithm>(
        env["BACKUP_KEY_DERIVATION_ALGORITHM"], ignoreCase: true, out var kdf))
    {
        config.KeyDerivationAlgorithm = kdf;
    }

    if (Enum.TryParse<BackupZCrypt.Domain.Enums.NameObfuscationMode>(
        env["BACKUP_NAME_OBFUSCATION"], ignoreCase: true, out var obf))
    {
        config.NameObfuscation = obf;
    }

    if (Enum.TryParse<BackupZCrypt.Domain.Enums.CompressionMode>(
        env["BACKUP_COMPRESSION"], ignoreCase: true, out var comp))
    {
        config.Compression = comp;
    }

    if (bool.TryParse(env["BACKUP_DELETE_SOURCE_FILES"], out var del))
    {
        config.DeleteSourceFiles = del;
    }
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
