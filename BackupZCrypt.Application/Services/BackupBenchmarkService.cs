using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Application.ValueObjects.Benchmark;
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
        if (request.DataBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.DataBytes,
                "The amount of data to estimate for must be greater than zero."
            );
        }

        var encryptionStrategy = encryptionServiceFactory.Create(request.EncryptionAlgorithm);
        var compressionStrategy =
            request.Compression is CompressionMode.None
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

            var chunkSlots = ComputeChunkSlotCount(Environment.ProcessorCount);

            var throughput = await MeasureThroughputAsync(
                    sample,
                    ComputeWorkerCount(Environment.ProcessorCount),
                    chunkSlots,
                    encryptionKey,
                    nonceKey,
                    encryptionStrategy,
                    compressionStrategy,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var largeFileThroughput = await MeasureThroughputAsync(
                    sample,
                    1,
                    chunkSlots,
                    encryptionKey,
                    nonceKey,
                    encryptionStrategy,
                    compressionStrategy,
                    cancellationToken
                )
                .ConfigureAwait(false);

            return new BenchmarkEstimate(
                ComputeEstimatedDuration(keyDerivationDuration, throughput, request.DataBytes),
                throughput,
                keyDerivationDuration,
                request.DataBytes,
                ComputeEstimatedDuration(keyDerivationDuration, largeFileThroughput, request.DataBytes),
                largeFileThroughput
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
    /// Returns how many workers the timed pass runs at once on a machine with the given number of
    /// logical processors: as many as the file pipelines a real backup runs there, so the measured
    /// throughput is not multiplied by processors the backup would leave idle.
    /// </summary>
    /// <param name="processorCount">The number of logical processors available to the process.</param>
    /// <returns>The number of concurrent measurement workers.</returns>
    internal static int ComputeWorkerCount(int processorCount)
    {
        return FileParallelismPolicy.ForProcessorCount(processorCount);
    }

    /// <summary>
    /// Returns how many chunks the timed pass compresses and encrypts at once on a machine with the
    /// given number of logical processors: as many as a real backup does.
    /// </summary>
    /// <param name="processorCount">The number of logical processors available to the process.</param>
    /// <returns>The number of chunks processed at once.</returns>
    internal static int ComputeChunkSlotCount(int processorCount)
    {
        return FileParallelismPolicy.ChunksInFlightForProcessorCount(processorCount);
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
                .Take(1)
                .ConfigureAwait(false)
        )
        {
            fileHasher.AppendData(chunk.Span);

            await EncryptChunkAsync(
                    chunk,
                    SHA256.HashData(chunk.Span),
                    encryptionKey,
                    nonceKey,
                    encryptionStrategy,
                    compressionStrategy,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs the pipeline a real backup runs — readers that chunk and hash their file in order, sharing
    /// a bounded pool of chunk slots in which chunks are compressed and encrypted concurrently — for
    /// the measure window, and reports the aggregate rate at which source bytes were consumed.
    /// </summary>
    /// <param name="sample">The synthetic sample data each reader reads from.</param>
    /// <param name="readers">The number of files read at once.</param>
    /// <param name="chunkSlots">The number of chunks compressed and encrypted at once.</param>
    /// <param name="encryptionKey">The throwaway chunk encryption key.</param>
    /// <param name="nonceKey">The throwaway key used to derive per-chunk nonces.</param>
    /// <param name="encryptionStrategy">The encryption strategy under test.</param>
    /// <param name="compressionStrategy">The compression strategy under test, or <see langword="null"/> when disabled.</param>
    /// <param name="cancellationToken">A token to cancel the measurement.</param>
    /// <returns>The measured throughput in source bytes per second.</returns>
    private async Task<double> MeasureThroughputAsync(
        byte[] sample,
        int readers,
        int chunkSlots,
        byte[] encryptionKey,
        byte[] nonceKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
        using SemaphoreSlim slots = new(chunkSlots, chunkSlots);
        var stopwatch = Stopwatch.StartNew();
        var workers = new Task<long>[readers];

        for (var i = 0; i < readers; i++)
        {
            workers[i] = Task.Run(
                () =>
                    MeasureReaderAsync(
                        sample,
                        stopwatch,
                        slots,
                        encryptionKey,
                        nonceKey,
                        encryptionStrategy,
                        compressionStrategy,
                        cancellationToken
                    ),
                cancellationToken
            );
        }

        var processedPerReader = await Task.WhenAll(workers).ConfigureAwait(false);
        stopwatch.Stop();

        return processedPerReader.Sum() / stopwatch.Elapsed.TotalSeconds;
    }

    /// <summary>
    /// Replays the sample through the chunker until the shared stopwatch passes the measure window,
    /// adding every chunk to the running file hash in order and handing it to a chunk slot for hashing,
    /// compression, and encryption, then waits for the chunks still in flight.
    /// </summary>
    /// <param name="sample">The synthetic sample data to chunk.</param>
    /// <param name="stopwatch">The stopwatch shared by all readers that bounds the measure window.</param>
    /// <param name="slots">The chunk slots shared by all readers.</param>
    /// <param name="encryptionKey">The throwaway chunk encryption key.</param>
    /// <param name="nonceKey">The throwaway key used to derive per-chunk nonces.</param>
    /// <param name="encryptionStrategy">The encryption strategy under test.</param>
    /// <param name="compressionStrategy">The compression strategy under test, or <see langword="null"/> when disabled.</param>
    /// <param name="cancellationToken">A token to cancel the reader.</param>
    /// <returns>The number of source bytes this reader processed.</returns>
    private async Task<long> MeasureReaderAsync(
        byte[] sample,
        Stopwatch stopwatch,
        SemaphoreSlim slots,
        byte[] encryptionKey,
        byte[] nonceKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
        long processed = 0;
        List<Task> inFlight = [];

        try
        {
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
                    fileHasher.AppendData(chunk.Span);

                    await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

                    inFlight.Add(
                        Task.Run(
                            async () =>
                            {
                                try
                                {
                                    await EncryptChunkAsync(
                                            chunk,
                                            SHA256.HashData(chunk.Span),
                                            encryptionKey,
                                            nonceKey,
                                            encryptionStrategy,
                                            compressionStrategy,
                                            cancellationToken
                                        )
                                        .ConfigureAwait(false);
                                }
                                finally
                                {
                                    _ = slots.Release();
                                }
                            },
                            cancellationToken
                        )
                    );

                    processed += chunk.Length;
                    _ = inFlight.RemoveAll(static task => task.IsCompletedSuccessfully);
                }
            }
        }
        finally
        {
            await Task.WhenAll(inFlight).ConfigureAwait(false);
        }

        return processed;
    }

    /// <summary>
    /// Runs one hashed chunk through the rest of the production pipeline — nonce derivation, optional
    /// compression, and authenticated encryption — then discards the ciphertext.
    /// </summary>
    /// <remarks>
    /// Every intermediate buffer, including the chunk hash, nonce, associated data, and ciphertext, is zeroed
    /// in a <see langword="finally"/> block so the benchmark leaves no derived material in memory.
    /// </remarks>
    /// <param name="chunk">The chunk produced by the chunking strategy.</param>
    /// <param name="chunkHash">The chunk's SHA-256 content hash, owned by this call.</param>
    /// <param name="encryptionKey">The throwaway chunk encryption key.</param>
    /// <param name="nonceKey">The throwaway key used to derive the per-chunk nonce.</param>
    /// <param name="encryptionStrategy">The encryption strategy under test.</param>
    /// <param name="compressionStrategy">The compression strategy under test, or <see langword="null"/> when disabled.</param>
    /// <param name="cancellationToken">A token to cancel the compression step.</param>
    /// <returns>A task that completes when the chunk has been processed and its buffers cleared.</returns>
    private static async Task EncryptChunkAsync(
        ReadOnlyMemory<byte> chunk,
        byte[] chunkHash,
        byte[] encryptionKey,
        byte[] nonceKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy,
        CancellationToken cancellationToken
    )
    {
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
