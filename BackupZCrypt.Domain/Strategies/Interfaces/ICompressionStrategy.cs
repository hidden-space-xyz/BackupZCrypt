using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.Strategies.Interfaces;

/// <summary>
/// Compresses and decompresses streams for a specific compression mode.
/// </summary>
public interface ICompressionStrategy
{
    /// <summary>
    /// Gets the compression mode this strategy implements, used to select it by enum value.
    /// </summary>
    CompressionMode Id { get; }

    /// <summary>
    /// Compresses the input stream.
    /// </summary>
    /// <param name="inputStream">The stream containing the data to compress.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A stream that yields the compressed data.</returns>
    Task<Stream> CompressAsync(Stream inputStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decompresses the input stream.
    /// </summary>
    /// <param name="inputStream">The stream containing the compressed data.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A stream that yields the decompressed data.</returns>
    Task<Stream> DecompressAsync(Stream inputStream, CancellationToken cancellationToken = default);
}
