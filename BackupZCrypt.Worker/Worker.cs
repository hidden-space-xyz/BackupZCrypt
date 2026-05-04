namespace BackupZCrypt.Worker;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using Microsoft.Extensions.Options;

internal sealed partial class Worker(
    ILogger<Worker> logger,
    IOptions<WorkerConfiguration> options,
    IBackupOrchestrator orchestrator,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        var performed = false;

        if (HasFilesToBackup(config.BackupSourcePath))
        {
            await RunBackupAsync(config, stoppingToken);
            performed = true;
        }
        else
        {
            LogNoBackupFiles(config.BackupSourcePath);
        }

        if (HasFilesToRestore(config.RestoreSourcePath))
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
        LogStartingBackup(config.BackupSourcePath, config.BackupDestinationPath);

        var password = config.UseEncryption ? config.Password : string.Empty;

        BackupRequest request = new(
            config.BackupSourcePath,
            config.BackupDestinationPath,
            password,
            password,
            config.EncryptionAlgorithm,
            config.KeyDerivationAlgorithm,
            EncryptOperation.Encrypt,
            config.NameObfuscation,
            config.Compression,
            ProceedOnWarnings: true,
            UseEncryption: config.UseEncryption);

        var result = await orchestrator.ExecuteAsync(
            request,
            new Progress<BackupStatus>(status =>
                LogBackupProgress(status.ProcessedFiles, status.TotalFiles, status.ProcessedBytes)),
            cancellationToken);

        if (!result.IsSuccess)
        {
            LogOperationFailed("Backup", string.Join("; ", result.Errors));
            return;
        }

        var backup = result.Value;

        if (backup.HasErrors)
        {
            LogOperationErrors("Backup", string.Join("; ", backup.Errors));
            return;
        }

        LogOperationCompleted(
            "Backup",
            backup.ProcessedFiles,
            backup.TotalFiles,
            backup.TotalBytes,
            backup.ElapsedTime);

        if (config.DeleteSourceFiles)
        {
            DeleteDirectoryContents(config.BackupSourcePath);
        }
    }

    private async Task RunRestoreAsync(
        WorkerConfiguration config,
        CancellationToken cancellationToken)
    {
        LogStartingRestore(config.RestoreSourcePath, config.RestoreDestinationPath);

        var isEncrypted = DetectEncryptedManifest(config.RestoreSourcePath);

        var password = isEncrypted ? config.Password : string.Empty;

        BackupRequest request;

        if (isEncrypted)
        {
            request = new(
                config.RestoreSourcePath,
                config.RestoreDestinationPath,
                password,
                password,
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.Argon2id,
                EncryptOperation.Decrypt,
                NameObfuscationMode.None);
        }
        else
        {
            request = new(
                config.RestoreSourcePath,
                config.RestoreDestinationPath,
                string.Empty,
                string.Empty,
                default,
                default,
                EncryptOperation.Decrypt,
                NameObfuscationMode.None,
                CompressionMode.None,
                ProceedOnWarnings: true,
                UseEncryption: false);
        }

        var result = await orchestrator.ExecuteAsync(
            request,
            new Progress<BackupStatus>(status =>
                LogRestoreProgress(status.ProcessedFiles, status.TotalFiles, status.ProcessedBytes)),
            cancellationToken);

        if (!result.IsSuccess)
        {
            LogOperationFailed("Restore", string.Join("; ", result.Errors));
            return;
        }

        var restore = result.Value;

        if (restore.HasErrors)
        {
            LogOperationErrors("Restore", string.Join("; ", restore.Errors));
            return;
        }

        LogOperationCompleted(
            "Restore",
            restore.ProcessedFiles,
            restore.TotalFiles,
            restore.TotalBytes,
            restore.ElapsedTime);

        if (config.DeleteSourceFiles)
        {
            DeleteDirectoryContents(config.RestoreSourcePath);
        }
    }

    private static bool HasFilesToBackup(string path)
    {
        return Directory.Exists(path)
            && Directory.EnumerateFileSystemEntries(path).Any();
    }

    private static bool HasFilesToRestore(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        var manifestPath = Path.Combine(path, BackupConstants.ManifestFileName);
        return File.Exists(manifestPath);
    }

    private static bool DetectEncryptedManifest(string sourcePath)
    {
        var manifestPath = Path.Combine(sourcePath, BackupConstants.ManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using FileStream fs = new(
                manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var firstByte = fs.ReadByte();
            return firstByte >= 0 && firstByte != '{';
        }
        catch
        {
            return false;
        }
    }

    private void DeleteDirectoryContents(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);

            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Delete();
            }

            foreach (var subDir in dir.EnumerateDirectories())
            {
                subDir.Delete(recursive: true);
            }

            LogDeletedSourceFiles(path);
        }
        catch (Exception ex)
        {
            LogDeleteSourceFilesFailed(ex, path);
        }
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
        Message = "Starting backup from '{Source}' to '{Destination}'.")]
    private partial void LogStartingBackup(string source, string destination);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Starting restore from '{Source}' to '{Destination}'.")]
    private partial void LogStartingRestore(string source, string destination);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Backup progress: {Processed}/{Total} files, {Bytes} bytes.")]
    private partial void LogBackupProgress(int processed, int total, long bytes);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Restore progress: {Processed}/{Total} files, {Bytes} bytes.")]
    private partial void LogRestoreProgress(int processed, int total, long bytes);

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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Deleted source files in '{Path}'.")]
    private partial void LogDeletedSourceFiles(string path);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Failed to delete source files in '{Path}'.")]
    private partial void LogDeleteSourceFilesFailed(Exception ex, string path);
}
