namespace BackupZCrypt.Infrastructure.Strategies.Compression;

/// <summary>
/// Zstandard compression strategy favouring speed over ratio (level 1).
/// </summary>
internal sealed class ZstdFastCompressionStrategy : ZstdCompressionStrategyBase
{
    /// <summary>
    /// Gets the compression-mode identifier (<see cref="Domain.Enums.CompressionMode.ZstdFast"/>) for this strategy.
    /// </summary>
    public override Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.ZstdFast;

    /// <summary>
    /// Gets the zstd compression level (1, fastest).
    /// </summary>
    protected override int CompressionLevel => 1;
}
