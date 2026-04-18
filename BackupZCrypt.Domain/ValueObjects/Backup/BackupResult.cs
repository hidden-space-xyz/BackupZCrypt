namespace BackupZCrypt.Domain.ValueObjects.Backup;

using BackupZCrypt.Domain.Enums;

public sealed record BackupResult
{
    public BackupResult(
        bool isSuccess,
        TimeSpan elapsedTime,
        long totalBytes,
        int processedFiles,
        int totalFiles,
        IEnumerable<string>? errors = null,
        IEnumerable<string>? warnings = null)
    {
        ValidateInputs(elapsedTime, totalBytes, processedFiles, totalFiles);

        this.IsSuccess = isSuccess;
        this.ElapsedTime = elapsedTime;
        this.TotalBytes = totalBytes;
        this.ProcessedFiles = processedFiles;
        this.TotalFiles = totalFiles;
        this.Errors = errors?.ToArray() ?? Array.Empty<string>();
        this.Warnings = warnings?.ToArray() ?? Array.Empty<string>();
    }

    public bool IsSuccess { get; }

    public TimeSpan ElapsedTime { get; }

    public long TotalBytes { get; }

    public int ProcessedFiles { get; }

    public int TotalFiles { get; }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyList<string> Warnings { get; }

    public bool HasErrors => this.Errors.Count > 0;

    public bool HasWarnings => this.Warnings.Count > 0;

    public int FailedFiles => this.TotalFiles - this.ProcessedFiles;

    public double SuccessRate => this.TotalFiles == 0 ? 1.0 : (double)this.ProcessedFiles / this.TotalFiles;

    public bool IsPartialSuccess => this.ProcessedFiles > 0 && this.ProcessedFiles < this.TotalFiles;

    public double BytesPerSecond =>
        this.ElapsedTime.TotalSeconds > 0 ? this.TotalBytes / this.ElapsedTime.TotalSeconds : 0;

    public double FilesPerSecond =>
        this.ElapsedTime.TotalSeconds > 0 ? this.ProcessedFiles / this.ElapsedTime.TotalSeconds : 0;

    private static void ValidateInputs(
        TimeSpan elapsedTime,
        long totalBytes,
        int processedFiles,
        int totalFiles)
    {
        if (elapsedTime < TimeSpan.Zero)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.ElapsedTimeNegative,
                "Elapsed time cannot be negative",
                nameof(elapsedTime));
        }

        if (totalBytes < 0)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.TotalBytesNegative,
                "Total bytes cannot be negative",
                nameof(totalBytes));
        }

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

        if (processedFiles > totalFiles)
        {
            throw new Exceptions.ValidationException(
                ValidationErrorCode.ProcessedFilesExceedTotalFiles,
                "Processed files cannot exceed total files",
                nameof(processedFiles));
        }
    }
}
