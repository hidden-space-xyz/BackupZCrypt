namespace BackupZCrypt.Infrastructure.Strategies.Compression;

/// <summary>
/// Zstandard compression strategy favouring ratio over speed (level 19).
/// </summary>
internal sealed class ZstdBestCompressionStrategy : ZstdCompressionStrategyBase
{
    /// <summary>
    /// Gets the compression-mode identifier (<see cref="Domain.Enums.CompressionMode.ZstdBest"/>) for this strategy.
    /// </summary>
    public override Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.ZstdBest;

    /// <summary>
    /// Gets the zstd compression level (19, maximum ratio).
    /// </summary>
    protected override int CompressionLevel => 19;
}
