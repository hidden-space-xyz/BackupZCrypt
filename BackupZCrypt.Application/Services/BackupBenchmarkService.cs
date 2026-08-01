using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Services;

/// <summary>
/// Estimates backup processing time by running the real chunking, hashing, compression, encryption,
/// and key-derivation strategies against synthetic, partially compressible data on the current
/// machine, then extrapolating the measured throughput to the requested amount of data. The data is
/// never written to disk and all key material is zeroed after use; deduplication is deliberately not
/// applied so the measured throughput reflects unique (worst-case) data.
/// </summary>
/// <param name="encryptionServiceFactory">The factory producing encryption strategies for an algorithm.</param>
/// <param name="compressionServiceFactory">The factory producing compression strategies for a compression mode.</param>
/// <param name="chunkingStrategy">The strategy used to split the synthetic stream into content-defined chunks.</param>
/// <param name="keyDerivationServiceFactory">The factory producing key derivation services for an algorithm.</param>
internal sealed class BackupBenchmarkService(
    IEncryptionServiceFactory encryptionServiceFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IChunkingStrategy chunkingStrategy,
    IKeyDerivationServiceFactory keyDerivationServiceFactory
) : IBackupBenchmarkService
{
    /// <summary>
    /// The length in bytes of the throwaway keys used by the benchmark, converted from the configured key size in bits.
    /// </summary>
    private const int KeySizeBytes = EncryptionConstants.KeySize / 8;

    /// <summary>
    /// The size of the synthetic sample buffer, large enough that a single pass yields several content-defined chunks.
    /// </summary>
    private const int SampleSizeBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The length of the block repeated through the second half of the sample, which makes the data partially
    /// compressible instead of incompressible noise.
    /// </summary>
    private const int RepeatBlockSize = 4096;

    /// <summary>
    /// The throwaway password fed to the key derivation strategy; it never protects real data.
    /// </summary>
    private const string SamplePassword = "benchmark-sample-password";

    /// <summary>
    /// How long the timed pass runs before the workers stop, kept short so the estimate stays responsive.
    /// </summary>
    private static readonly TimeSpan MeasureWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The largest duration a <see cref="TimeSpan"/> can represent, in seconds, used as the clamp for estimates.
    /// </summary>
    private static readonly double MaxEstimateSeconds = TimeSpan.MaxValue.TotalSeconds;

    /// <summary>
    /// Runs the benchmark for the requested options and extrapolates the result to the requested
    /// amount of data.
    /// </summary>
    /// <param name="request">The cryptographic options to exercise and the amount of data to estimate for.</param>
    /// <param name="cancellationToken">A token to cancel the benchmark.</param>
    /// <returns>An estimate of the total processing time and the measurements it was derived from.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="BenchmarkRequest.DataBytes"/> is not greater than zero, or the requested encryption
    /// algorithm is not registered.
    /// </exception>
    public async Task<BenchmarkEstimate> EstimateAsync(
        BenchmarkRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.DataBytes);

        var encryptionStrategy = encryptionServiceFactory.Create(request.EncryptionAlgorithm);
        var compressionStrategy =
            request.Compression == CompressionMode.None
                ? null
                : compressionServiceFactory.Create(request.Compression);
        var keyDerivationStrategy = keyDerivationServiceFactory.Create(
            request.KeyDerivationAlgorithm
        );

        var sample = CreateSampleData(SampleSizeBytes);
        byte[]? salt = null;
        byte[]? encryptionKey = null;
        byte[]? nonceKey = null;

        try
        {
            salt = RandomNumberGenerator.GetBytes(EncryptionConstants.SaltSize);
            encryptionKey = RandomNumberGenerator.GetBytes(KeySizeBytes);
            nonceKey = RandomNumberGenerator.GetBytes(KeySizeBytes);

            var keyDerivationDuration = MeasureKeyDerivation(keyDerivationStrategy, salt);

            await WarmUpAsync(
                    sample,
                    encryptionKey,
                    nonceKey,
                    encryptionStrategy,
                    compressionStrategy,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var throughput = await MeasureThroughputAsync(
                    sample,
                    encryptionKey,
                    nonceKey,
                    encryptionStrategy,
                    compressionStrategy,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var estimatedDuration = ComputeEstimatedDuration(
                keyDerivationDuration,
                throughput,
                request.DataBytes
            );

            return new BenchmarkEstimate(
                estimatedDuration,
                throughput,
                keyDerivationDuration,
                request.DataBytes
            );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sample);

            if (salt is not null)
            {
                CryptographicOperations.ZeroMemory(salt);
            }

            if (encryptionKey is not null)
            {
                CryptographicOperations.ZeroMemory(encryptionKey);
            }

            if (nonceKey is not null)
            {
                CryptographicOperations.ZeroMemory(nonceKey);
            }
        }
    }

    /// <summary>
    /// Combines the one-time key-derivation cost with the time to process the requested data at the
    /// measured throughput, clamping to <see cref="TimeSpan.MaxValue"/> rather than overflowing.
    /// </summary>
    /// <param name="keyDerivationDuration">The measured one-time key-derivation cost.</param>
    /// <param name="throughputBytesPerSecond">The measured processing throughput in source bytes per second.</param>
    /// <param name="dataBytes">The amount of source data to estimate for, in bytes.</param>
    /// <returns>The estimated total duration, or <see cref="TimeSpan.MaxValue"/> when it would overflow.</returns>
    internal static TimeSpan ComputeEstimatedDuration(
        TimeSpan keyDerivationDuration,
        double throughputBytesPerSecond,
        long dataBytes
    )
    {
        if (throughputBytesPerSecond <= 0)
        {
            return TimeSpan.MaxValue;
        }

        var seconds =
            keyDerivationDuration.TotalSeconds + (dataBytes / throughputBytesPerSecond);

        return seconds >= MaxEstimateSeconds
            ? TimeSpan.MaxValue
            : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Times a single master-key derivation with the selected algorithm and zeroes the derived key.
    /// </summary>
    /// <param name="keyDerivationStrategy">The key derivation strategy to exercise.</param>
    /// <param name="salt">The random salt handed to the derivation.</param>
    /// <returns>The elapsed time of the one-time key derivation.</returns>
    private static TimeSpan MeasureKeyDerivation(
        IKeyDerivationAlgorithmStrategy keyDerivationStrategy,
        byte[] salt
    )
    {
        var stopwatch = Stopwatch.StartNew();
        var key = keyDerivationStrategy.DeriveKey(SamplePassword, salt, EncryptionConstants.KeySize);
        stopwatch.Stop();
        CryptographicOperations.ZeroMemory(key);
        return stopwatch.Elapsed;
    }

    /// <summary>
    /// Runs the pipeline over a single chunk so that JIT compilation and first-use allocations do not
    /// distort the timed measurement that follows.
    /// </summary>
    /// <param name="sample">The synthetic sample data to chunk.</param>
    /// <param name="encryptionKey">The throwaway chunk encryption key.</param>
    /// <param name="nonceKey">The throwaway key used to derive per-chunk nonces.</param>
    /// <param name="encryptionStrategy">The encryption strategy under test.</param>
    /// <param name="compressionStrategy">The compression strategy under test, or <see langword="null"/> when disabled.</param>
    /// <param name="cancellationToken">A token to cancel the warm-up.</param>
    /// <returns>A task that completes once one chunk has been processed.</returns>
    private async Task WarmUpAsync(
        byte[] sample,
        byte[] encryptionKey,
        byte[] nonceKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
        await using var stream = new MemoryStream(sample, 0, sample.Length, writable: false);
        using var fileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await foreach (
            var chunk in chunkingStrategy
                .ChunkAsync(stream, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            await ProcessChunkAsync(
                    chunk,
                    fileHasher,
                    encryptionKey,
                    nonceKey,
                    encryptionStrategy,
                    compressionStrategy,
                    cancellationToken
                )
                .ConfigureAwait(false);

            break;
        }
    }

    /// <summary>
    /// Saturates every logical processor with the chunk pipeline for the measure window and reports the
    /// aggregate rate at which source bytes were consumed.
    /// </summary>
    /// <param name="sample">The synthetic sample data each worker reads from.</param>
    /// <param name="encryptionKey">The throwaway chunk encryption key.</param>
    /// <param name="nonceKey">The throwaway key used to derive per-chunk nonces.</param>
    /// <param name="encryptionStrategy">The encryption strategy under test.</param>
    /// <param name="compressionStrategy">The compression strategy under test, or <see langword="null"/> when disabled.</param>
    /// <param name="cancellationToken">A token to cancel the measurement.</param>
    /// <returns>The measured throughput in source bytes per second.</returns>
    private async Task<double> MeasureThroughputAsync(
        byte[] sample,
        byte[] encryptionKey,
        byte[] nonceKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
        var workerCount = Math.Max(1, Environment.ProcessorCount);
        var stopwatch = Stopwatch.StartNew();
        var workers = new Task<long>[workerCount];

        for (var i = 0; i < workerCount; i++)
        {
            workers[i] = Task.Run(
                () =>
                    MeasureWorkerAsync(
                        sample,
                        stopwatch,
                        encryptionKey,
                        nonceKey,
                        encryptionStrategy,
                        compressionStrategy,
                        cancellationToken
                    ),
                cancellationToken
            );
        }

        var processedPerWorker = await Task.WhenAll(workers).ConfigureAwait(false);
        stopwatch.Stop();

        var totalProcessed = processedPerWorker.Sum();
        return totalProcessed / stopwatch.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// Replays the sample through the chunk pipeline until the shared stopwatch passes the measure window,
    /// counting the source bytes this worker consumed.
    /// </summary>
    /// <param name="sample">The synthetic sample data to chunk.</param>
    /// <param name="stopwatch">The stopwatch shared by all workers that bounds the measure window.</param>
    /// <param name="encryptionKey">The throwaway chunk encryption key.</param>
    /// <param name="nonceKey">The throwaway key used to derive per-chunk nonces.</param>
    /// <param name="encryptionStrategy">The encryption strategy under test.</param>
    /// <param name="compressionStrategy">The compression strategy under test, or <see langword="null"/> when disabled.</param>
    /// <param name="cancellationToken">A token to cancel the worker.</param>
    /// <returns>The number of source bytes this worker processed.</returns>
    private async Task<long> MeasureWorkerAsync(
        byte[] sample,
        Stopwatch stopwatch,
        byte[] encryptionKey,
        byte[] nonceKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
        long processed = 0;

        while (stopwatch.Elapsed < MeasureWindow)
        {
            await using var stream = new MemoryStream(sample, 0, sample.Length, writable: false);
            using var fileHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await foreach (
                var chunk in chunkingStrategy
                    .ChunkAsync(stream, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                await ProcessChunkAsync(
                        chunk,
                        fileHasher,
                        encryptionKey,
                        nonceKey,
                        encryptionStrategy,
                        compressionStrategy,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                processed += chunk.Length;
            }
        }

        return processed;
    }

    /// <summary>
    /// Runs one chunk through the full production pipeline — file hashing, chunk hashing, nonce derivation,
    /// optional compression, and authenticated encryption — then discards the ciphertext.
    /// </summary>
    /// <remarks>
    /// Every intermediate buffer, including the chunk hash, nonce, associated data, and ciphertext, is zeroed
    /// in a <c>finally</c> block so the benchmark leaves no derived material in memory.
    /// </remarks>
    /// <param name="chunk">The chunk produced by the chunking strategy.</param>
    /// <param name="fileHasher">The running whole-file hash the chunk is appended to.</param>
    /// <param name="encryptionKey">The throwaway chunk encryption key.</param>
    /// <param name="nonceKey">The throwaway key used to derive the per-chunk nonce.</param>
    /// <param name="encryptionStrategy">The encryption strategy under test.</param>
    /// <param name="compressionStrategy">The compression strategy under test, or <see langword="null"/> when disabled.</param>
    /// <param name="cancellationToken">A token to cancel the compression step.</param>
    /// <returns>A task that completes when the chunk has been processed and its buffers cleared.</returns>
    private static async Task ProcessChunkAsync(
        ReadOnlyMemory<byte> chunk,
        IncrementalHash fileHasher,
        byte[] encryptionKey,
        byte[] nonceKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
        fileHasher.AppendData(chunk.Span);

        var chunkHash = SHA256.HashData(chunk.Span);
        byte[]? nonce = null;
        byte[]? associatedData = null;
        byte[]? compressed = null;
        byte[]? encrypted = null;

        try
        {
            nonce = ChunkCryptoHelper.ComputeChunkNonce(nonceKey, chunkHash);

            if (compressionStrategy is not null)
            {
                compressed = await CompressChunkAsync(compressionStrategy, chunk, cancellationToken)
                    .ConfigureAwait(false);
            }

            associatedData = ChunkCryptoHelper.BuildChunkAssociatedData(chunkHash, nonce);
            encrypted = compressed is not null
                ? encryptionStrategy.EncryptChunk(compressed, encryptionKey, nonce, associatedData)
                : encryptionStrategy.EncryptChunk(
                    chunk.Span,
                    encryptionKey,
                    nonce,
                    associatedData
                );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chunkHash);

            if (nonce is not null)
            {
                CryptographicOperations.ZeroMemory(nonce);
            }

            if (associatedData is not null)
            {
                CryptographicOperations.ZeroMemory(associatedData);
            }

            if (compressed is not null)
            {
                CryptographicOperations.ZeroMemory(compressed);
            }

            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }
    }

    /// <summary>
    /// Compresses a chunk entirely in memory and returns the compressed bytes.
    /// </summary>
    /// <param name="compressionStrategy">The compression strategy under test.</param>
    /// <param name="chunk">The chunk to compress.</param>
    /// <param name="cancellationToken">A token to cancel the compression.</param>
    /// <returns>The compressed representation of the chunk.</returns>
    private static async Task<byte[]> CompressChunkAsync(
        ICompressionStrategy compressionStrategy,
        ReadOnlyMemory<byte> chunk,
        CancellationToken cancellationToken
    )
    {
        await using var input = new MemoryStream(chunk.ToArray(), writable: false);

        await using var compressed = await compressionStrategy
            .CompressAsync(input, cancellationToken)
            .ConfigureAwait(false);

        await using MemoryStream buffer = new();
        await compressed.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>
    /// Builds a synthetic buffer whose first half is filled by a fast xorshift generator and whose second half
    /// repeats a fixed block of those bytes, giving data that compresses partially rather than not at all.
    /// </summary>
    /// <param name="size">The length of the buffer to create, in bytes.</param>
    /// <returns>The synthetic sample data.</returns>
    private static byte[] CreateSampleData(int size)
    {
        var data = GC.AllocateUninitializedArray<byte>(size);
        var span = data.AsSpan();
        var state = 0x9E3779B97F4A7C15UL;
        var offset = 0;

        while (offset + sizeof(ulong) <= size)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            BinaryPrimitives.WriteUInt64LittleEndian(span[offset..], state);
            offset += sizeof(ulong);
        }

        var half = size / 2;
        for (var i = half; i < size; i++)
        {
            data[i] = data[(i - half) % RepeatBlockSize];
        }

        return data;
    }
}
