using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Domain.ValueObjects.Backup;

/// <summary>
/// The immutable outcome of a completed backup or restore operation, including timing,
/// counts, and any accumulated errors and warnings.
/// </summary>
public sealed record class BackupResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackupResult"/> class.
    /// </summary>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="elapsedTime">The total time the operation took.</param>
    /// <param name="totalBytes">The total number of bytes processed.</param>
    /// <param name="processedFiles">The number of files processed successfully.</param>
    /// <param name="totalFiles">The total number of files in the operation.</param>
    /// <param name="errors">The errors that occurred, if any.</param>
    /// <param name="warnings">The warnings that were raised, if any.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="elapsedTime"/> is negative, a byte or file count is negative, or
    /// <paramref name="processedFiles"/> exceeds <paramref name="totalFiles"/>.
    /// </exception>
    public BackupResult(
        bool isSuccess,
        TimeSpan elapsedTime,
        long totalBytes,
        int processedFiles,
        int totalFiles,
        IEnumerable<LocalizableMessage>? errors = null,
        IEnumerable<LocalizableMessage>? warnings = null
    )
    {
        ValidateInputs(elapsedTime, totalBytes, processedFiles, totalFiles);

        this.IsSuccess = isSuccess;
        this.ElapsedTime = elapsedTime;
        this.TotalBytes = totalBytes;
        this.ProcessedFiles = processedFiles;
        this.TotalFiles = totalFiles;
        this.Errors = errors?.ToArray() ?? (LocalizableMessage[])[];
        this.Warnings = warnings?.ToArray() ?? (LocalizableMessage[])[];
    }

    /// <summary>
    /// Gets a value indicating whether the operation completed successfully.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets the total time the operation took.
    /// </summary>
    public TimeSpan ElapsedTime { get; }

    /// <summary>
    /// Gets the total number of bytes processed.
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// Gets the number of files processed successfully.
    /// </summary>
    public int ProcessedFiles { get; }

    /// <summary>
    /// Gets the total number of files in the operation.
    /// </summary>
    public int TotalFiles { get; }

    /// <summary>
    /// Gets the errors that occurred during the operation, if any.
    /// </summary>
    public IReadOnlyList<LocalizableMessage> Errors { get; }

    /// <summary>
    /// Gets the warnings that were raised during the operation, if any.
    /// </summary>
    public IReadOnlyList<LocalizableMessage> Warnings { get; }

    /// <summary>
    /// Gets a value indicating whether any errors were recorded.
    /// </summary>
    public bool HasErrors => this.Errors.Count > 0;

    /// <summary>
    /// Gets a value indicating whether any warnings were recorded.
    /// </summary>
    public bool HasWarnings => this.Warnings.Count > 0;

    /// <summary>
    /// Gets the number of files that failed to process.
    /// </summary>
    public int FailedFiles => this.TotalFiles - this.ProcessedFiles;

    /// <summary>
    /// Gets the fraction of files processed successfully, from 0 to 1; 1 when there were no files.
    /// </summary>
    public double SuccessRate =>
        this.TotalFiles is 0 ? 1.0 : (double)this.ProcessedFiles / this.TotalFiles;

    /// <summary>
    /// Gets a value indicating whether some, but not all, files were processed successfully.
    /// </summary>
    public bool IsPartialSuccess =>
        this.ProcessedFiles > 0 && this.ProcessedFiles < this.TotalFiles;

    /// <summary>
    /// Gets the average throughput in bytes per second, or 0 when no time elapsed.
    /// </summary>
    public double BytesPerSecond =>
        this.ElapsedTime.TotalSeconds > 0 ? this.TotalBytes / this.ElapsedTime.TotalSeconds : 0;

    /// <summary>
    /// Gets the average throughput in files per second, or 0 when no time elapsed.
    /// </summary>
    public double FilesPerSecond =>
        this.ElapsedTime.TotalSeconds > 0 ? this.ProcessedFiles / this.ElapsedTime.TotalSeconds : 0;

    /// <summary>
    /// Guards the constructor arguments, rejecting negative values and a processed file count
    /// that exceeds the total.
    /// </summary>
    /// <param name="elapsedTime">The total time the operation took.</param>
    /// <param name="totalBytes">The total number of bytes processed.</param>
    /// <param name="processedFiles">The number of files processed successfully.</param>
    /// <param name="totalFiles">The total number of files in the operation.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A value is negative or <paramref name="processedFiles"/> exceeds <paramref name="totalFiles"/>.
    /// </exception>
    private static void ValidateInputs(
        TimeSpan elapsedTime,
        long totalBytes,
        int processedFiles,
        int totalFiles
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsedTime, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(processedFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(totalFiles);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(processedFiles, totalFiles);
    }
}
