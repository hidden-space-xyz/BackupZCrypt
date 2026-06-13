namespace BackupZCrypt.Infrastructure.Strategies.Compression;

/// <summary>
/// Zstandard compression strategy balancing speed and ratio (level 3, the zstd default).
/// </summary>
internal sealed class ZstdCompressionStrategy : ZstdCompressionStrategyBase
{
    /// <summary>
    /// Gets the compression-mode identifier (<see cref="Domain.Enums.CompressionMode.Zstd"/>) for this strategy.
    /// </summary>
    public override Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.Zstd;

    /// <summary>
    /// Gets the zstd compression level (3, balanced).
    /// </summary>
    protected override int CompressionLevel => 3;
}
