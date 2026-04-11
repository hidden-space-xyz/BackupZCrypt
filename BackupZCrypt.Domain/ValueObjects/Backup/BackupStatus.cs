namespace BackupZCrypt.Domain.ValueObjects.Backup;

using BackupZCrypt.Domain.Enums;

public sealed record BackupStatus
{
    public BackupStatus(
        int processedFiles,
        int totalFiles,
        long processedBytes,
        long totalBytes,
        TimeSpan elapsed)
    {
        if (processedFiles < 0)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.ProcessedFilesNegative,
                "Processed files cannot be negative",
                nameof(processedFiles));
        }

        if (totalFiles < 0)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.TotalFilesNegative,
                "Total files cannot be negative",
                nameof(totalFiles));
        }

        if (processedBytes < 0)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.ProcessedBytesNegative,
                "Processed bytes cannot be negative",
                nameof(processedBytes));
        }

        if (totalBytes < 0)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.TotalBytesNegative,
                "Total bytes cannot be negative",
                nameof(totalBytes));
        }

        if (elapsed < TimeSpan.Zero)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.ElapsedNegative,
                "Elapsed time cannot be negative",
                nameof(elapsed));
        }

        if (processedFiles > totalFiles)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.ProcessedFilesExceedTotalFiles,
                "Processed files cannot exceed total files",
                nameof(processedFiles));
        }

        if (processedBytes > totalBytes)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.ProcessedBytesExceedTotalBytes,
                "Processed bytes cannot exceed total bytes",
                nameof(processedBytes));
        }

        this.ProcessedFiles = processedFiles;
        this.TotalFiles = totalFiles;
        this.ProcessedBytes = processedBytes;
        this.TotalBytes = totalBytes;
        this.Elapsed = elapsed;
    }

    public int ProcessedFiles { get; }

    public int TotalFiles { get; }

    public long ProcessedBytes { get; }

    public long TotalBytes { get; }

    public TimeSpan Elapsed { get; }
}
