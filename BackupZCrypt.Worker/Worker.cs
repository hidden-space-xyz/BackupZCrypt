namespace BackupZCrypt.Worker;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Worker.Services;
using Microsoft.Extensions.Options;

internal sealed partial class Worker(
    ILogger<Worker> logger,
    IOptions<WorkerConfiguration> options,
    IBackupOrchestrator orchestrator,
    IWorkerFileSystem fileSystem,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        var performed = false;

        if (fileSystem.HasFilesToBackup(config.BackupSourcePath))
        {
            await RunBackupAsync(config, stoppingToken);
            performed = true;
        }
        else
        {
            LogNoBackupFiles(config.BackupSourcePath);
        }

        if (fileSystem.HasFilesToRestore(config.RestoreSourcePath))
        {
            await RunRestoreAsync(config, stoppingToken);
            performed = true;
        }
        else
        {
            LogNoRestoreFiles(config.RestoreSourcePath);
        }

        if (!performed)
        {
            LogNothingToProcess();
        }

        lifetime.StopApplication();
    }

    private async Task RunBackupAsync(
        WorkerConfiguration config,
        CancellationToken cancellationToken)
    {
        LogStartingOperation("Backup", config.BackupSourcePath, config.BackupDestinationPath);

        var password = config.EncryptionAlgorithm != EncryptionAlgorithm.None ? config.Password : string.Empty;

        BackupRequest request = new(
            config.BackupSourcePath,
            config.BackupDestinationPath,
            password,
            password,
            config.EncryptionAlgorithm,
            config.KeyDerivationAlgorithm,
            BackupOperation.Create,
            config.Compression,
            ProceedOnWarnings: true);

        var result = await ExecuteOperationAsync("Backup", request, cancellationToken);

        if (result is not null && config.DeleteSourceFiles)
        {
            fileSystem.DeleteDirectoryContents(config.BackupSourcePath);
        }
    }

    private async Task RunRestoreAsync(
        WorkerConfiguration config,
        CancellationToken cancellationToken)
    {
        LogStartingOperation("Restore", config.RestoreSourcePath, config.RestoreDestinationPath);

        var isEncrypted = fileSystem.IsManifestEncrypted(config.RestoreSourcePath);
        var password = isEncrypted ? config.Password : string.Empty;
        var encryptionAlgorithm = isEncrypted ? config.EncryptionAlgorithm : EncryptionAlgorithm.None;

        BackupRequest request = new(
            config.RestoreSourcePath,
            config.RestoreDestinationPath,
            password,
            password,
            encryptionAlgorithm,
            config.KeyDerivationAlgorithm,
            BackupOperation.Restore,
            config.Compression,
            ProceedOnWarnings: true);

        var result = await ExecuteOperationAsync("Restore", request, cancellationToken);

        if (result is not null && config.DeleteSourceFiles)
        {
            fileSystem.DeleteDirectoryContents(config.RestoreSourcePath);
        }
    }

    private async Task<BackupResult?> ExecuteOperationAsync(
        string operationName,
        BackupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await orchestrator.ExecuteAsync(
            request,
            new Progress<BackupStatus>(status =>
                LogProgress(operationName, status.ProcessedFiles, status.TotalFiles, status.ProcessedBytes)),
            cancellationToken);

        if (!result.IsSuccess)
        {
            LogOperationFailed(operationName, string.Join("; ", result.Errors));
            return null;
        }

        var operation = result.Value;

        if (operation.HasErrors)
        {
            LogOperationErrors(operationName, string.Join("; ", operation.Errors));
            return null;
        }

        LogOperationCompleted(
            operationName,
            operation.ProcessedFiles,
            operation.TotalFiles,
            operation.TotalBytes,
            operation.ElapsedTime);

        return operation;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No files found in backup source '{Path}'. Skipping backup.")]
    private partial void LogNoBackupFiles(string path);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No files found in restore source '{Path}'. Skipping restore.")]
    private partial void LogNoRestoreFiles(string path);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Nothing to process. Stopping.")]
    private partial void LogNothingToProcess();

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Starting {Operation} from '{Source}' to '{Destination}'.")]
    private partial void LogStartingOperation(string operation, string source, string destination);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "{Operation} progress: {Processed}/{Total} files, {Bytes} bytes.")]
    private partial void LogProgress(string operation, int processed, int total, long bytes);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "{Operation} failed: {Errors}")]
    private partial void LogOperationFailed(string operation, string errors);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "{Operation} completed with errors: {Errors}")]
    private partial void LogOperationErrors(string operation, string errors);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "{Operation} completed: {Processed}/{Total} files, {Bytes} bytes in {Elapsed}.")]
    private partial void LogOperationCompleted(
        string operation, int processed, int total, long bytes, TimeSpan elapsed);
}
