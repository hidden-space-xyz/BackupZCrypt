namespace BackupZCrypt.Infrastructure.Strategies.Compression;

internal sealed class ZstdCompressionStrategy : ZstdCompressionStrategyBase
{
    public override Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.Zstd;

    protected override int CompressionLevel => 3;
}
