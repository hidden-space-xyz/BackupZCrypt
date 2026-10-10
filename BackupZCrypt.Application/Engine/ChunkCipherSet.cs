using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Carries the three chunk sub-keys and the two strategies one chunking pass runs under, so a
/// single value reaches every chunk of every file instead of five parallel arguments.
/// </summary>
/// <remarks>
/// The set holds the very arrays <see cref="ArchiveKeys"/> owns rather than copies of them, so
/// it neither extends the lifetime of any key nor changes when one is wiped: the operation's
/// <see cref="ArchiveKeys"/> is still the only owner and still zeroes all of them on dispose.
/// </remarks>
/// <param name="chunkEncryptionKey">The sub-key chunk contents are encrypted with.</param>
/// <param name="chunkNonceKey">The sub-key per-chunk nonces are derived from.</param>
/// <param name="namingKey">The sub-key chunk file names are derived from.</param>
/// <param name="encryptionStrategy">The strategy used to encrypt chunks.</param>
/// <param name="compressionStrategy">The strategy applied before encryption, or <see langword="null"/> to skip compression.</param>
internal sealed class ChunkCipherSet(
    byte[] chunkEncryptionKey,
    byte[] chunkNonceKey,
    byte[] namingKey,
    IEncryptionAlgorithmStrategy encryptionStrategy,
    ICompressionStrategy? compressionStrategy
)
{
    /// <summary>
    /// Gets the sub-key used to encrypt chunk contents.
    /// </summary>
    public byte[] ChunkEncryptionKey { get; } = chunkEncryptionKey;

    /// <summary>
    /// Gets the sub-key each chunk's deterministic nonce is derived from.
    /// </summary>
    public byte[] ChunkNonceKey { get; } = chunkNonceKey;

    /// <summary>
    /// Gets the sub-key each chunk's on-disk file name is derived from.
    /// </summary>
    public byte[] NamingKey { get; } = namingKey;

    /// <summary>
    /// Gets the strategy used to encrypt chunks.
    /// </summary>
    public IEncryptionAlgorithmStrategy EncryptionStrategy { get; } = encryptionStrategy;

    /// <summary>
    /// Gets the strategy applied before encryption, or <see langword="null"/> when compression is disabled.
    /// </summary>
    public ICompressionStrategy? CompressionStrategy { get; } = compressionStrategy;
}
