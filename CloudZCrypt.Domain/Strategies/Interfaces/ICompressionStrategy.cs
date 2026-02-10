using CloudZCrypt.Domain.Enums;

namespace CloudZCrypt.Domain.Strategies.Interfaces;

public interface ICompressionStrategy
{
    CompressionMode Id { get; }
    string DisplayName { get; }
    string Description { get; }
    string Summary { get; }

    Task<Stream> CompressAsync(Stream inputStream, CancellationToken cancellationToken = default);
    Task<Stream> DecompressAsync(Stream inputStream, CancellationToken cancellationToken = default);
}
