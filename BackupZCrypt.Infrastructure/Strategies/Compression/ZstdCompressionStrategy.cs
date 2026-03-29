namespace BackupZCrypt.Infrastructure.Strategies.Compression;

using BackupZCrypt.Infrastructure.Resources;

internal sealed class ZstdCompressionStrategy : ZstdCompressionStrategyBase
{
    public override Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.Zstd;

    public override string DisplayName => Messages.ZstdDisplayName;

    public override string Description => Messages.ZstdDescription;

    public override string Summary => Messages.ZstdSummary;

    protected override int CompressionLevel => 3;
}
