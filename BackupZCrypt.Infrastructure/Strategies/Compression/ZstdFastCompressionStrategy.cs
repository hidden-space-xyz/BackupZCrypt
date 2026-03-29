namespace BackupZCrypt.Infrastructure.Strategies.Compression;

using BackupZCrypt.Infrastructure.Resources;

internal sealed class ZstdFastCompressionStrategy : ZstdCompressionStrategyBase
{
    public override Domain.Enums.CompressionMode Id => Domain.Enums.CompressionMode.ZstdFast;

    public override string DisplayName => Messages.ZstdFastDisplayName;

    public override string Description => Messages.ZstdFastDescription;

    public override string Summary => Messages.ZstdFastSummary;

    protected override int CompressionLevel => 1;
}
