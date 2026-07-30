namespace BackupZCrypt.Domain.Constants;

/// <summary>
/// Tuning constants for stream-based I/O.
/// </summary>
public static class StreamConstants
{
    /// <summary>
    /// The buffer size in bytes (80 KiB) used for stream copies, pooled read buffers, and file stream buffering.
    /// The value matches the default buffer size the BCL uses for stream copies.
    /// </summary>
    public const int CopyBufferSize = 80 * 1024;
}
