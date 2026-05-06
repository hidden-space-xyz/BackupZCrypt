namespace BackupZCrypt.Domain.ValueObjects.Backup;

public sealed record BackupStatus
{
    public BackupStatus(
        int processedFiles,
        int totalFiles,
        long processedBytes,
        long totalBytes,
        TimeSpan elapsed)
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

    public int ProcessedFiles { get; }

    public int TotalFiles { get; }

    public long ProcessedBytes { get; }

    public long TotalBytes { get; }

    public TimeSpan Elapsed { get; }
}
