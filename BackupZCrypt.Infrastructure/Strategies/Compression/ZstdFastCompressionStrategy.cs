namespace BackupZCrypt.Infrastructure.Strategies.Compression;

internal sealed class ZstdFastCompressionStrategy : ZstdCompressionStrategyBase
{
    public override Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.ZstdFast;

    protected override int CompressionLevel => 1;
}
