using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Engine;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Benchmark;

/// <summary>
/// Estimates backup processing time by running the real chunking, hashing, compression, encryption,
/// and key-derivation strategies against synthetic, partially compressible data on the current
/// machine, then extrapolating the measured throughput to the requested amount of data. The data is
/// never written to disk and all key material is zeroed after use; deduplication is deliberately not
/// applied so the measured throughput reflects unique (worst-case) data.
/// </summary>
/// <param name="encryptionStrategies">The registered encryption strategies, looked up by algorithm.</param>
/// <param name="compressionStrategies">The registered compression strategies, looked up by mode.</param>
/// <param name="chunkingStrategy">The strategy used to split the synthetic stream into content-defined chunks.</param>
/// <param name="keyDerivationStrategies">The registered key derivation strategies, looked up by algorithm.</param>
internal sealed class BackupBenchmarkService(
    IStrategyRegistry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy> encryptionStrategies,
    IStrategyRegistry<CompressionMode, ICompressionStrategy> compressionStrategies,
    IChunkingStrategy chunkingStrategy,
    IStrategyRegistry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy> keyDerivationStrategies
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
    /// amount of data, reporting a benchmark that fails as a failed result.
    /// </summary>
    /// <param name="request">The cryptographic options to exercise and the amount of data to estimate for.</param>
    /// <param name="cancellationToken">A token to cancel the benchmark.</param>
    /// <returns>
    /// The estimate, or a failure carrying the reason the benchmark could not run, such as an
    /// algorithm that is not registered.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="BenchmarkRequest.DataBytes"/> is not greater than zero.
    /// </exception>
    public async Task<Result<BenchmarkEstimate>> EstimateAsync(
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

        try
        {
            return await MeasureAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            return Result<BenchmarkEstimate>.Failure(
                MessageCode.UnexpectedErrorFormat,
                exception.Message
            );
        }
    }

    /// <summary>
    /// Measures the requested options on synthetic data and extrapolates the result to the requested
    /// amount of data.
    /// </summary>
    /// <param name="request">The validated options to exercise and the amount of data to estimate for.</param>
    /// <param name="cancellationToken">A token to cancel the benchmark.</param>
    /// <returns>An estimate of the total processing time and the measurements it was derived from.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An algorithm the request names is not registered.</exception>
    private async Task<BenchmarkEstimate> MeasureAsync(
        BenchmarkRequest request,
        CancellationToken cancellationToken
    )
    {
        var encryptionStrategy = encryptionStrategies.Resolve(request.EncryptionAlgorithm);
        var compressionStrategy = compressionStrategies.ResolveOrNull(request.Compression);
        var keyDerivationStrategy = keyDerivationStrategies.Resolve(
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
            ChunkSealer sealer = new(encryptionKey, nonceKey, encryptionStrategy, compressionStrategy);

            await WarmUpAsync(sample, sealer, cancellationToken).ConfigureAwait(false);

            var chunkSlots = ComputeChunkSlotCount(Environment.ProcessorCount);

            var throughput = await MeasureThroughputAsync(
                    sample,
                    ComputeWorkerCount(Environment.ProcessorCount),
                    chunkSlots,
                    sealer,
                    cancellationToken
                )
                .ConfigureAwait(false);

            var largeFileThroughput = await MeasureThroughputAsync(
                    sample,
                    1,
                    chunkSlots,
                    sealer,
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
    /// <param name="sealer">The sealer under test, holding the throwaway keys.</param>
    /// <param name="cancellationToken">A token to cancel the warm-up.</param>
    /// <returns>A task that completes once one chunk has been processed.</returns>
    private async Task WarmUpAsync(byte[] sample, ChunkSealer sealer, CancellationToken cancellationToken)
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

            await SealAndDiscardAsync(sealer, chunk, cancellationToken).ConfigureAwait(false);
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
    /// <param name="sealer">The sealer under test, holding the throwaway keys.</param>
    /// <param name="cancellationToken">A token to cancel the measurement.</param>
    /// <returns>The measured throughput in source bytes per second.</returns>
    private async Task<double> MeasureThroughputAsync(
        byte[] sample,
        int readers,
        int chunkSlots,
        ChunkSealer sealer,
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
                        sealer,
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
    /// <param name="sealer">The sealer under test, holding the throwaway keys.</param>
    /// <param name="cancellationToken">A token to cancel the reader.</param>
    /// <returns>The number of source bytes this reader processed.</returns>
    private async Task<long> MeasureReaderAsync(
        byte[] sample,
        Stopwatch stopwatch,
        SemaphoreSlim slots,
        ChunkSealer sealer,
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
                                    await SealAndDiscardAsync(sealer, chunk, cancellationToken)
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
    /// Hashes one chunk and runs it through the sealer a real backup stores chunks with, then discards
    /// the stored bytes.
    /// </summary>
    /// <remarks>
    /// The chunk hash and the stored bytes are zeroed in a <see langword="finally"/> block so the
    /// benchmark leaves no derived material in memory; the sealer wipes its own intermediate buffers.
    /// </remarks>
    /// <param name="sealer">The sealer under test, holding the throwaway keys.</param>
    /// <param name="chunk">The chunk produced by the chunking strategy.</param>
    /// <param name="cancellationToken">A token to cancel the compression step.</param>
    /// <returns>A task that completes when the chunk has been sealed and its buffers cleared.</returns>
    private static async Task SealAndDiscardAsync(
        ChunkSealer sealer,
        ReadOnlyMemory<byte> chunk,
        CancellationToken cancellationToken
    )
    {
        var chunkHash = SHA256.HashData(chunk.Span);
        byte[]? storedChunk = null;

        try
        {
            storedChunk = await sealer.SealAsync(chunk, chunkHash, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chunkHash);

            if (storedChunk is not null)
            {
                CryptographicOperations.ZeroMemory(storedChunk);
            }
        }
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
