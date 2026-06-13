namespace BackupZCrypt.Infrastructure.Strategies.Compression;

internal sealed class ZstdBestCompressionStrategy : ZstdCompressionStrategyBase
{
    public override Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.ZstdBest;

    protected override int CompressionLevel => 19;
}
