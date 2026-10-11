using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Domain.Archive;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Turns a chunk's plaintext into the bytes an archive stores for it and back: optional compression
/// followed by authenticated encryption under the chunk's deterministic nonce, with the chunk hash
/// and that nonce bound in as associated data.
/// </summary>
/// <remarks>
/// <para>
/// This is the one implementation of the stored chunk layout. The writer, the reader, and the
/// benchmark all go through it, so what a backup writes, what a restore accepts, and what the
/// benchmark times cannot drift apart.
/// </para>
/// <para>
/// The sealer holds the caller's key arrays rather than copies of them, such as the ones
/// <see cref="ArchiveKeys"/> owns, so it neither extends the lifetime of any key nor survives its
/// wiping: whoever owns the keys still zeroes them. The nonce, the associated data, the decrypted
/// chunk, and the compressed copy of the chunk are zeroed on every path, as is the in-memory buffer
/// compression wrote into whenever it exposes one; buffers a compression or decompression stream
/// keeps internally are beyond its reach. The plaintext, the chunk hash, and the stored bytes belong
/// to the caller.
/// </para>
/// </remarks>
/// <param name="encryptionKey">The sub-key chunk contents are encrypted with.</param>
/// <param name="nonceKey">The sub-key each chunk's nonce is derived from.</param>
/// <param name="encryptionStrategy">The cipher chunks are encrypted with.</param>
/// <param name="compressionStrategy">The compression applied before encryption, or <see langword="null"/> to store chunks uncompressed.</param>
internal sealed class ChunkSealer(
    byte[] encryptionKey,
    byte[] nonceKey,
    IEncryptionAlgorithmStrategy encryptionStrategy,
    ICompressionStrategy? compressionStrategy
)
{
    /// <summary>
    /// Returns the largest stored length a chunk of the given plaintext size can have.
    /// </summary>
    /// <remarks>
    /// An uncompressed chunk is stored as exactly its plaintext plus the authentication tag. A
    /// compressed one can be any length up to <see cref="EngineLimits.MaximumStoredChunkSize"/>.
    /// </remarks>
    /// <param name="chunkSize">The plaintext length the manifest records for the chunk.</param>
    /// <returns>The greatest stored length to accept and allocate for the chunk.</returns>
    /// <exception cref="OverflowException">The size cannot be represented with the tag added.</exception>
    internal int MaximumStoredSize(int chunkSize)
    {
        return compressionStrategy is null
            ? checked(chunkSize + EncryptionConstants.TagSize)
            : EngineLimits.MaximumStoredChunkSize;
    }

    /// <summary>
    /// Determines whether a stored chunk file of the given length could hold a chunk of the given
    /// plaintext size, so a truncated, padded, or oversized file is refused before it is read.
    /// </summary>
    /// <param name="storedSize">The length of the stored chunk file.</param>
    /// <param name="chunkSize">The plaintext length the manifest records for the chunk.</param>
    /// <returns><see langword="true"/> when the length is possible for the chunk.</returns>
    /// <exception cref="OverflowException">The size cannot be represented with the tag added.</exception>
    internal bool IsPlausibleStoredSize(long storedSize, int chunkSize)
    {
        var maximumStoredSize = this.MaximumStoredSize(chunkSize);

        return storedSize >= EncryptionConstants.TagSize
            && storedSize <= maximumStoredSize
            && (compressionStrategy is not null || storedSize == maximumStoredSize);
    }

    /// <summary>
    /// Compresses a chunk when compression is enabled and encrypts it into the bytes stored on disk.
    /// </summary>
    /// <remarks>
    /// Compression runs before encryption so ciphertext is never compressed. The nonce is derived
    /// deterministically from the chunk hash, which lets identical content deduplicate while keeping
    /// the nonce key-dependent.
    /// </remarks>
    /// <param name="chunk">The plaintext of the chunk, owned by the caller.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk, owned by the caller.</param>
    /// <param name="cancellationToken">A token to cancel the compression.</param>
    /// <returns>The stored bytes, which the caller owns and must zero.</returns>
    /// <exception cref="InvalidDataException">The compressed chunk would exceed the largest stored chunk the reader accepts.</exception>
    internal async Task<byte[]> SealAsync(
        ReadOnlyMemory<byte> chunk,
        byte[] chunkHash,
        CancellationToken cancellationToken
    )
    {
        var nonce = ChunkCrypto.ComputeChunkNonce(nonceKey, chunkHash);
        byte[]? compressed = null;
        byte[]? associatedData = null;

        try
        {
            if (compressionStrategy is not null)
            {
                compressed = await CompressAsync(compressionStrategy, chunk, cancellationToken)
                    .ConfigureAwait(false);

                if (compressed.Length > EngineLimits.MaximumStoredChunkSize - EncryptionConstants.TagSize)
                {
                    throw new InvalidDataException("Compressed chunk exceeds the supported size.");
                }
            }

            associatedData = ChunkCrypto.BuildChunkAssociatedData(chunkHash, nonce);

            return compressed is not null
                ? encryptionStrategy.EncryptChunk(compressed, encryptionKey, nonce, associatedData)
                : encryptionStrategy.EncryptChunk(chunk.Span, encryptionKey, nonce, associatedData);
        }
        finally
        {
            if (compressed is not null)
            {
                CryptographicOperations.ZeroMemory(compressed);
            }

            if (associatedData is not null)
            {
                CryptographicOperations.ZeroMemory(associatedData);
            }

            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    /// <summary>
    /// Authenticates and decrypts stored chunk bytes, decompresses them when compression is enabled,
    /// and writes the plaintext to <paramref name="destination"/> while appending it to
    /// <paramref name="hasher"/>.
    /// </summary>
    /// <remarks>
    /// A tampered chunk fails authentication before any of it reaches <paramref name="destination"/>,
    /// and decompression is bounded by <paramref name="chunkSize"/> so a crafted chunk cannot expand
    /// past the size the manifest declares.
    /// </remarks>
    /// <param name="storedChunk">The stored bytes of the chunk, owned by the caller.</param>
    /// <param name="chunkHash">The SHA-256 content hash the manifest records for the chunk, owned by the caller.</param>
    /// <param name="chunkSize">The plaintext length the manifest records for the chunk.</param>
    /// <param name="destination">The stream the plaintext is written to.</param>
    /// <param name="hasher">The running file hash the plaintext is appended to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of plaintext bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="CryptographicException">The chunk fails authentication or its plaintext length differs from <paramref name="chunkSize"/>.</exception>
    /// <exception cref="InvalidDataException">The chunk decompresses beyond <paramref name="chunkSize"/>.</exception>
    internal async Task<long> OpenAsync(
        byte[] storedChunk,
        byte[] chunkHash,
        int chunkSize,
        Stream destination,
        IncrementalHash hasher,
        CancellationToken cancellationToken
    )
    {
        var nonce = ChunkCrypto.ComputeChunkNonce(nonceKey, chunkHash);
        byte[]? associatedData = null;
        byte[]? decrypted = null;

        try
        {
            associatedData = ChunkCrypto.BuildChunkAssociatedData(chunkHash, nonce);
            decrypted = encryptionStrategy.DecryptChunk(
                storedChunk,
                encryptionKey,
                nonce,
                associatedData
            );

            if (compressionStrategy is not null)
            {
                await using MemoryStream compressedStream = new(decrypted, writable: false);

                await using var decompressedStream = await compressionStrategy
                    .DecompressAsync(compressedStream, cancellationToken)
                    .ConfigureAwait(false);

                var decompressedSize = await CopyToWithHashAsync(
                        decompressedStream,
                        destination,
                        hasher,
                        chunkSize,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                if (decompressedSize != chunkSize)
                {
                    throw new CryptographicException(
                        "Decompressed chunk size does not match the manifest."
                    );
                }

                return decompressedSize;
            }

            if (decrypted.Length != chunkSize)
            {
                throw new CryptographicException("Chunk size does not match the manifest.");
            }

            hasher.AppendData(decrypted);
            await destination.WriteAsync(decrypted, cancellationToken).ConfigureAwait(false);

            return decrypted.Length;
        }
        finally
        {
            if (decrypted is not null)
            {
                CryptographicOperations.ZeroMemory(decrypted);
            }

            if (associatedData is not null)
            {
                CryptographicOperations.ZeroMemory(associatedData);
            }

            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    /// <summary>
    /// Compresses a chunk entirely in memory, wiping the in-memory buffers the compressed bytes passed
    /// through, and the returned copy too when compression fails after it was made.
    /// </summary>
    /// <param name="compressionStrategy">The compression to apply.</param>
    /// <param name="chunk">The plaintext of the chunk.</param>
    /// <param name="cancellationToken">A token to cancel the compression.</param>
    /// <returns>The compressed bytes, which the caller owns and must zero.</returns>
    private static async Task<byte[]> CompressAsync(
        ICompressionStrategy compressionStrategy,
        ReadOnlyMemory<byte> chunk,
        CancellationToken cancellationToken
    )
    {
        byte[]? compressed = null;

        try
        {
            await using (var input = CreateReadOnlyStream(chunk))
            await using (
                var compressedStream = await compressionStrategy
                    .CompressAsync(input, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                compressed = compressedStream is MemoryStream compressedMemory
                    ? CopyAndWipe(compressedMemory)
                    : await BufferAndWipeAsync(compressedStream, cancellationToken)
                        .ConfigureAwait(false);
            }

            return compressed;
        }
        catch
        {
            if (compressed is not null)
            {
                CryptographicOperations.ZeroMemory(compressed);
            }

            throw;
        }
    }

    /// <summary>
    /// Reads a stream to its end into a new array through an in-memory buffer that is wiped on every
    /// path.
    /// </summary>
    /// <param name="source">The stream to read.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The bytes read.</returns>
    private static async Task<byte[]> BufferAndWipeAsync(Stream source, CancellationToken cancellationToken)
    {
        await using MemoryStream buffer = new();

        try
        {
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

            return buffer.ToArray();
        }
        finally
        {
            Wipe(buffer);
        }
    }

    /// <summary>
    /// Copies the contents of a memory stream into a new array, then zeroes the stream's buffer.
    /// </summary>
    /// <param name="stream">The stream whose contents are copied.</param>
    /// <returns>The copied contents.</returns>
    private static byte[] CopyAndWipe(MemoryStream stream)
    {
        try
        {
            return stream.ToArray();
        }
        finally
        {
            Wipe(stream);
        }
    }

    /// <summary>
    /// Zeroes the contents of a memory stream's buffer when the stream exposes it.
    /// </summary>
    /// <param name="stream">The stream whose buffer is zeroed.</param>
    private static void Wipe(MemoryStream stream)
    {
        if (stream.TryGetBuffer(out var segment))
        {
            CryptographicOperations.ZeroMemory(segment.Array!.AsSpan(segment.Offset, segment.Count));
        }
    }

    /// <summary>
    /// Wraps a block of memory in a non-writable stream, reusing the backing array when the memory
    /// exposes one so chunk data is not copied.
    /// </summary>
    /// <param name="data">The bytes to expose as a stream.</param>
    /// <returns>A read-only stream over the data.</returns>
    private static MemoryStream CreateReadOnlyStream(ReadOnlyMemory<byte> data)
    {
        return MemoryMarshal.TryGetArray(data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);
    }

    /// <summary>
    /// Copies a stream to a destination while appending the copied bytes to a running hash, stopping
    /// with an error if the copy would exceed the caller's byte budget.
    /// </summary>
    /// <remarks>
    /// The budget bounds decompression to the size the manifest declares for the chunk, so a crafted
    /// chunk cannot expand without limit. The pooled buffer is zeroed before it is returned.
    /// </remarks>
    /// <param name="source">The stream to read from.</param>
    /// <param name="destination">The stream the bytes are written to.</param>
    /// <param name="hasher">The running hash the copied bytes are appended to.</param>
    /// <param name="maxBytes">The maximum number of bytes this copy is allowed to produce.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of bytes copied.</returns>
    /// <exception cref="InvalidDataException">The source yields more than <paramref name="maxBytes"/> bytes.</exception>
    private static async Task<long> CopyToWithHashAsync(
        Stream source,
        Stream destination,
        IncrementalHash hasher,
        long maxBytes,
        CancellationToken cancellationToken
    )
    {
        var buffer = ArrayPool<byte>.Shared.Rent(StreamConstants.CopyBufferSize);
        long total = 0;

        try
        {
            while (true)
            {
                var read = await source
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (read is 0)
                {
                    return total;
                }

                total += read;

                if (total > maxBytes)
                {
                    throw new InvalidDataException(
                        "Decompressed data exceeds the size declared in the manifest."
                    );
                }

                hasher.AppendData(buffer.AsSpan(0, read));
                await destination
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, buffer.Length));
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
