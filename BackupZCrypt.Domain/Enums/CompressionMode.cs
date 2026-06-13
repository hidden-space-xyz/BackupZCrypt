namespace BackupZCrypt.Domain.Enums;

/// <summary>
/// Selects whether and how aggressively chunks are compressed before encryption.
/// </summary>
public enum CompressionMode
{
    /// <summary>
    /// No compression is applied.
    /// </summary>
    None = 0,

    /// <summary>
    /// Zstandard compression tuned for speed over ratio.
    /// </summary>
    ZstdFast = 1,

    /// <summary>
    /// Zstandard compression at the default balance of speed and ratio.
    /// </summary>
    Zstd = 2,

    /// <summary>
    /// Zstandard compression tuned for the best ratio over speed.
    /// </summary>
    ZstdBest = 3,
}
