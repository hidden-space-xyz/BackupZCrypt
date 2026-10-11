using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Carries what the chunks of one archive are stored and found with: the sealer that turns a chunk
/// into its stored bytes and back, and the sub-key its on-disk file name is derived from.
/// </summary>
/// <remarks>
/// The set holds the very arrays <see cref="ArchiveKeys"/> owns rather than copies of them, so
/// it neither extends the lifetime of any key nor changes when one is wiped: the operation's
/// <see cref="ArchiveKeys"/> is still the only owner and still zeroes all of them on dispose.
/// It is only built by <see cref="For"/>, so the sealer and the naming key always come from the
/// same key schedule.
/// </remarks>
internal sealed class ChunkCipherSet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkCipherSet"/> class.
    /// </summary>
    /// <param name="sealer">The sealer chunks are stored and read with.</param>
    /// <param name="namingKey">The sub-key chunk file names are derived from.</param>
    private ChunkCipherSet(ChunkSealer sealer, byte[] namingKey)
    {
        this.Sealer = sealer;
        this.NamingKey = namingKey;
    }

    /// <summary>
    /// Gets the sealer chunks are stored and read with.
    /// </summary>
    public ChunkSealer Sealer { get; }

    /// <summary>
    /// Gets the sub-key each chunk's on-disk file name is derived from.
    /// </summary>
    public byte[] NamingKey { get; }

    /// <summary>
    /// Builds the set for an archive from its key schedule and the strategies its manifest names.
    /// </summary>
    /// <param name="keys">The archive's keys, which stay owned by the caller.</param>
    /// <param name="encryptionStrategy">The cipher the archive's chunks are encrypted with.</param>
    /// <param name="compressionStrategy">The compression applied before encryption, or <see langword="null"/> when disabled.</param>
    /// <returns>The set sharing the caller's key arrays.</returns>
    public static ChunkCipherSet For(
        ArchiveKeys keys,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy
    )
    {
        ArgumentNullException.ThrowIfNull(keys);

        return new(
            new ChunkSealer(
                keys.ChunkEncryptionKey,
                keys.ChunkNonceKey,
                encryptionStrategy,
                compressionStrategy
            ),
            keys.NamingKey
        );
    }
}
