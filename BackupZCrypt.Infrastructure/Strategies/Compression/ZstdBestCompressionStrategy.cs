using BackupZCrypt.Infrastructure.Resources;

namespace BackupZCrypt.Infrastructure.Strategies.Compression;

internal sealed class ZstdBestCompressionStrategy : ZstdCompressionStrategyBase
{
    public override Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.ZstdBest;

    public override string DisplayName => Messages.ZstdBestDisplayName;

    public override string Description => Messages.ZstdBestDescription;

    public override string Summary => Messages.ZstdBestSummary;

    protected override int CompressionLevel => 19;
}
