namespace BackupZCrypt.Domain.ValueObjects.Backup;

/// <summary>
/// An immutable snapshot of the progress of an in-flight backup or restore operation.
/// </summary>
public sealed record class BackupStatus
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackupStatus"/> class.
    /// </summary>
    /// <param name="processedFiles">The number of files processed so far.</param>
    /// <param name="totalFiles">The total number of files to process.</param>
    /// <param name="processedBytes">The number of bytes processed so far.</param>
    /// <param name="totalBytes">The total number of bytes to process.</param>
    /// <param name="elapsed">The time elapsed since the operation started.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A count or byte total is negative, <paramref name="elapsed"/> is negative, or a processed value exceeds its total.
    /// </exception>
    public BackupStatus(
        int processedFiles,
        int totalFiles,
        long processedBytes,
        long totalBytes,
        TimeSpan elapsed
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(processedFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(totalFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(processedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(totalBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(processedFiles, totalFiles);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(processedBytes, totalBytes);

        this.ProcessedFiles = processedFiles;
        this.TotalFiles = totalFiles;
        this.ProcessedBytes = processedBytes;
        this.TotalBytes = totalBytes;
        this.Elapsed = elapsed;
    }

    /// <summary>
    /// Gets the number of files processed so far.
    /// </summary>
    public int ProcessedFiles { get; }

    /// <summary>
    /// Gets the total number of files to process.
    /// </summary>
    public int TotalFiles { get; }

    /// <summary>
    /// Gets the number of bytes processed so far.
    /// </summary>
    public long ProcessedBytes { get; }

    /// <summary>
    /// Gets the total number of bytes to process.
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// Gets the time elapsed since the operation started.
    /// </summary>
    public TimeSpan Elapsed { get; }
}
