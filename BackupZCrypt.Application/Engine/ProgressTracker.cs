using System.Diagnostics;

using BackupZCrypt.Application.Common;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Reports the progress of a run as bytes are read and files complete. The processed byte count
/// is capped at the total measured before the run, so a file that grows while it is read never
/// produces an impossible report.
/// </summary>
/// <param name="progress">The sink that receives status updates.</param>
/// <param name="stopwatch">The running timer of the operation.</param>
/// <param name="totalFiles">The number of files in the run.</param>
/// <param name="totalBytes">The size of the files measured before the run.</param>
internal sealed class ProgressTracker(
    IProgress<BackupStatus>? progress,
    Stopwatch stopwatch,
    int totalFiles,
    long totalBytes
)
{
    /// <summary>
    /// The bytes read so far.
    /// </summary>
    private long processedBytes;

    /// <summary>
    /// The files completed so far.
    /// </summary>
    private int processedFiles;

    /// <summary>
    /// Reports the empty starting status.
    /// </summary>
    public void ReportStart()
    {
        progress?.Report(new BackupStatus(0, totalFiles, 0, totalBytes, TimeSpan.Zero));
    }

    /// <summary>
    /// Records bytes read from a file.
    /// </summary>
    /// <param name="bytes">The number of bytes read.</param>
    public void AddBytes(long bytes)
    {
        var current = Interlocked.Add(ref this.processedBytes, bytes);
        this.Report(Volatile.Read(ref this.processedFiles), current);
    }

    /// <summary>
    /// Records a completed file.
    /// </summary>
    public void CompleteFile()
    {
        var files = Interlocked.Increment(ref this.processedFiles);
        this.Report(files, Volatile.Read(ref this.processedBytes));
    }

    /// <summary>
    /// Sends one status report.
    /// </summary>
    /// <param name="files">The files completed so far.</param>
    /// <param name="bytes">The bytes read so far.</param>
    private void Report(int files, long bytes)
    {
        progress?.Report(
            new BackupStatus(
                Math.Min(files, totalFiles),
                totalFiles,
                Math.Clamp(bytes, 0, totalBytes),
                totalBytes,
                stopwatch.Elapsed
            )
        );
    }
}
