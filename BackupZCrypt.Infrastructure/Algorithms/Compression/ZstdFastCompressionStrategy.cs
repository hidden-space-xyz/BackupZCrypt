namespace BackupZCrypt.Infrastructure.Algorithms.Compression;

/// <summary>
/// Zstandard compression strategy favoring speed over ratio (level 1).
/// </summary>
internal sealed class ZstdFastCompressionStrategy : ZstdCompressionStrategyBase
{
    /// <summary>
    /// Gets the compression-mode identifier (<see cref="Domain.Algorithms.CompressionMode.ZstdFast"/>) for this strategy.
    /// </summary>
    public override Domain.Algorithms.CompressionMode Id => Domain.Algorithms.CompressionMode.ZstdFast;

    /// <summary>
    /// Gets the zstd compression level (1, fastest).
    /// </summary>
    protected override int CompressionLevel => 1;
}
