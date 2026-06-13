using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.Strategies.Interfaces;

public interface ICompressionStrategy
{
    CompressionMode Id { get; }

    Task<Stream> CompressAsync(Stream inputStream, CancellationToken cancellationToken = default);

    Task<Stream> DecompressAsync(Stream inputStream, CancellationToken cancellationToken = default);
}
