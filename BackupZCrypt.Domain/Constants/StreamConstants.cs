namespace BackupZCrypt.Domain.Constants;

/// <summary>
/// Tuning constants for stream-based I/O.
/// </summary>
public static class StreamConstants
{
    /// <summary>
    /// Buffer size in bytes used when copying data between streams.
    /// </summary>
    public const int CopyBufferSize = 80 * 1024;
}
