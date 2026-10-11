namespace BackupZCrypt.Infrastructure.Algorithms.Compression;

/// <summary>
/// Zstandard compression strategy favoring ratio over speed (level 19).
/// </summary>
internal sealed class ZstdBestCompressionStrategy : ZstdCompressionStrategyBase
{
    /// <summary>
    /// Gets the compression-mode identifier (<see cref="Domain.Algorithms.CompressionMode.ZstdBest"/>) for this strategy.
    /// </summary>
    public override Domain.Algorithms.CompressionMode Id => Domain.Algorithms.CompressionMode.ZstdBest;

    /// <summary>
    /// Gets the zstd compression level (19, maximum ratio).
    /// </summary>
    protected override int CompressionLevel => 19;
}
