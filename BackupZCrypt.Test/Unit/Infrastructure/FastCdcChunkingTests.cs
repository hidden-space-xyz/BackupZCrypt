using System.Globalization;
using System.Security.Cryptography;

using BackupZCrypt.Infrastructure.Strategies.Chunking;

namespace BackupZCrypt.Test.Unit.Infrastructure;

/// <summary>
/// Unit tests for the FastCDC content-defined chunking strategy.
/// </summary>
/// <remarks>
/// The cut-point scan consumes two bytes per step and tests a mask after each one, so its four arms
/// pair two mask constants with differently shifted hashes. The golden sample only ever cuts on an
/// even offset while below the target size, which is why a second fixed input pins an odd cut point
/// below that target: swapping in the pre-shifted mask on that arm, or returning the step's even
/// offset, still produces a valid backup, so no other case in this file would fail.
/// </remarks>
public sealed class FastCdcChunkingTests
{
    /// <summary>
    /// The 4 MiB upper bound the strategy enforces on any chunk, mirrored here because the
    /// production constant is private.
    /// </summary>
    /// <remarks>
    /// Random data always finds a cut point first, so only degenerate input - a sparse VM image, a
    /// zero-filled database file - ever reaches this hard cap, which is what stops the chunker from
    /// buffering without bound.
    /// </remarks>
    private const int ChunkMaxSize = 4 * 1024 * 1024;

    /// <summary>
    /// The 256 KiB lower bound the strategy enforces on every chunk but the last, mirrored here
    /// because the production constant is private.
    /// </summary>
    private const int ChunkMinSize = 256 * 1024;

    /// <summary>
    /// The 1 MiB average chunk size, mirrored here because the production constant is private. Cut
    /// points below it are chosen with the strict mask and above it with the lenient one.
    /// </summary>
    /// <remarks>
    /// Input shorter than this target also exercises the branch that clamps the cut-point search
    /// window, which is why the size-bounds test includes a 512 KiB case.
    /// </remarks>
    private const int ChunkTargetSize = 1024 * 1024;

    /// <summary>
    /// The exact chunk lengths <see cref="BoundarySample"/> must split into. These values are part
    /// of the on-disk format: they are decided by the Gear table, the two cut-point masks, and the
    /// min/target/max constants, and moving any of them re-cuts every chunk so nothing deduplicates
    /// against backups that already exist.
    /// </summary>
    private static readonly int[] GoldenChunkLengths =
    [
        1164096, 1524042, 1209755, 1200141, 772520, 1379712, 1061387, 1078207, 920826, 175074,
    ];

    /// <summary>
    /// The 10 MiB seeded input behind the boundary-stability tests, generated once and shared
    /// because the tests only ever read it.
    /// </summary>
    private static readonly byte[] BoundarySample = RandomBytes(10 * 1024 * 1024, seed: 22);

    /// <summary>
    /// Produces input from a fixed seed so cut points, and therefore chunk sizes, are reproducible.
    /// </summary>
    /// <param name="length">The number of bytes to produce.</param>
    /// <param name="seed">The seed that makes the sequence deterministic.</param>
    /// <returns>A buffer of pseudo-random bytes.</returns>
    private static byte[] RandomBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>
    /// Chunks the stream and materializes each yielded memory block as an array so tests can compare
    /// contents directly.
    /// </summary>
    /// <param name="source">The stream to feed through the chunker.</param>
    /// <returns>The chunks in the order they were produced.</returns>
    private static async Task<List<byte[]>> DrainAsync(Stream source)
    {
        var strategy = new FastCdcChunkingStrategy();
        List<byte[]> chunks = [];

        await foreach (var chunk in strategy.ChunkAsync(source))
        {
            chunks.Add(chunk.ToArray());
        }

        return chunks;
    }

    /// <summary>
    /// Chunks a buffer through a <see cref="MemoryStream"/>, the shape most tests need.
    /// </summary>
    /// <param name="input">The bytes to feed through the chunker.</param>
    /// <returns>The chunks in the order they were produced.</returns>
    private static Task<List<byte[]>> DrainAsync(byte[] input)
    {
        return DrainAsync(new MemoryStream(input));
    }

    /// <summary>
    /// Hashes the chunks in order, which compares a split against the original far more cheaply than
    /// an element-by-element comparison of multi-megabyte arrays while proving the same thing.
    /// </summary>
    /// <param name="chunks">The chunks to hash, in order.</param>
    /// <returns>The SHA-256 hash of the concatenated chunks.</returns>
    private static byte[] HashOf(IEnumerable<byte[]> chunks)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var chunk in chunks)
        {
            hash.AppendData(chunk);
        }

        return hash.GetHashAndReset();
    }

    [Test]
    public async Task EmptyInput_YieldsNoChunks()
    {
        var chunks = await DrainAsync([]);

        Assert.That(chunks, Is.Empty);
    }

    [Test]
    public async Task SmallInput_YieldsSingleChunkEqualToInput()
    {
        var input = RandomBytes(10 * 1024, seed: 1);

        var chunks = await DrainAsync(input);

        Assert.That(chunks, Has.Count.EqualTo(1));
        Assert.That(chunks[0], Is.EqualTo(input));
    }

    [Test]
    public async Task LargeInput_ReassemblesToOriginal_WithMultipleChunks()
    {
        var input = RandomBytes(10 * 1024 * 1024, seed: 2);

        var chunks = await DrainAsync(input);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(chunks, Has.Count.GreaterThan(1), $"Expected multiple chunks, got {chunks.Count}.");
            Assert.That(chunks.Sum(chunk => chunk.Length), Is.EqualTo(input.Length));
            Assert.That(
                HashOf(chunks),
                Is.EqualTo(SHA256.HashData(input)),
                "The concatenated chunks are not byte-identical to the input."
            );
        }
    }

    [TestCase(512 * 1024)]
    [TestCase(10 * 1024 * 1024)]
    public async Task EveryChunk_StaysWithinTheConfiguredSizeBounds(int inputLength)
    {
        var chunks = await DrainAsync(RandomBytes(inputLength, seed: 3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                chunks,
                Has.All.Matches<byte[]>(chunk => chunk.Length <= ChunkMaxSize),
                "A chunk exceeded the maximum size, which is what bounds memory per concurrent file."
            );
            Assert.That(
                chunks.Take(chunks.Count - 1),
                Has.All.Matches<byte[]>(chunk => chunk.Length >= ChunkMinSize),
                "Only the final chunk may be shorter than the minimum chunk size."
            );
        }
    }

    [Test]
    public async Task FixedInput_ProducesTheGoldenChunkBoundaries()
    {
        var chunks = await DrainAsync(BoundarySample);

        Assert.That(
            chunks.Select(chunk => chunk.Length).ToArray(),
            Is.EqualTo(GoldenChunkLengths),
            "Chunk boundaries moved. That is an on-disk format change: chunks stored by earlier "
                + "versions will never be matched again, so every existing backup re-uploads in full."
        );
    }

    [Test]
    public async Task FixedInputCutBelowTheTargetSize_HonoursAnOddOffsetCutPoint()
    {
        var chunks = await DrainAsync(RandomBytes(ChunkTargetSize, seed: 20));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                chunks.Select(chunk => chunk.Length).ToArray(),
                Is.EqualTo([357111, 691465]),
                "The strict-mask cut point moved, which is an on-disk format change."
            );
            Assert.That(
                chunks[0].Length % 2,
                Is.EqualTo(1),
                "The first chunk no longer ends on an odd offset, so this test no longer covers the "
                    + "arm it exists for."
            );
            Assert.That(
                chunks[0],
                Has.Length.GreaterThan(ChunkMinSize),
                "The cut point fell below the window where the strict mask applies."
            );
            Assert.That(
                chunks[0],
                Has.Length.LessThan(ChunkTargetSize),
                "The cut point rose above the window where the strict mask applies."
            );
        }
    }

    [Test]
    public async Task UniformInput_IsCutAtTheMaximumChunkSize()
    {
        const int Length = 10 * 1024 * 1024;

        var chunks = await DrainAsync(new byte[Length]);

        Assert.That(
            chunks.Select(chunk => chunk.Length).ToArray(),
            Is.EqualTo([ChunkMaxSize, ChunkMaxSize, Length - (2 * ChunkMaxSize)]),
            "Input that never finds a cut point was not cut at the maximum chunk size, so the "
                + "chunker buffers without bound."
        );
    }

    [Test]
    public async Task ShortReads_ProduceTheSameBoundariesAsWholeBufferReads()
    {
        var input = RandomBytes(4 * 1024 * 1024, seed: 23);

        var reference = await DrainAsync(new MemoryStream(input));

        using var drippedStream = new DripStream(input, maxBytesPerRead: 997);
        var dripped = await DrainAsync(drippedStream);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                dripped.Select(chunk => chunk.Length).ToArray(),
                Is.EqualTo(reference.Select(chunk => chunk.Length).ToArray()),
                "Chunk boundaries changed when the source returned partial reads."
            );
            Assert.That(HashOf(dripped), Is.EqualTo(HashOf(reference)));
        }
    }

    [Test]
    public async Task ContentInsertedInTheMiddle_LeavesLaterChunksUnchanged()
    {
        const int InsertAt = 1024 * 1024;
        var insertion = RandomBytes(1024, seed: 24);
        var modified = new byte[BoundarySample.Length + insertion.Length];
        BoundarySample.AsSpan(0, InsertAt).CopyTo(modified);
        insertion.CopyTo(modified.AsSpan(InsertAt));
        BoundarySample.AsSpan(InsertAt).CopyTo(modified.AsSpan(InsertAt + insertion.Length));

        var originalHashes = (await DrainAsync(BoundarySample))
            .Select(chunk => Convert.ToHexString(SHA256.HashData(chunk)))
            .ToHashSet(StringComparer.Ordinal);
        var modifiedChunks = await DrainAsync(modified);

        var shared = modifiedChunks.Count(chunk =>
            originalHashes.Contains(Convert.ToHexString(SHA256.HashData(chunk)))
        );

        var sharedSummary = string.Create(
            CultureInfo.InvariantCulture,
            $"Only {shared} of {modifiedChunks.Count} chunks survived a 1 KiB insertion: the chunker "
        );

        Assert.That(
            shared,
            Is.GreaterThanOrEqualTo(6),
            sharedSummary
                + "no longer resynchronises, so deduplication across updates has collapsed. This is "
                + "the property content-defined chunking exists for, and every other case in this "
                + "file would still pass if the rolling hash degenerated into fixed-size splitting."
        );
    }

    [Test]
    public void Chunk_CancelledToken_ThrowsOperationCanceledException()
    {
        var strategy = new FastCdcChunkingStrategy();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _ = Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            var source = new MemoryStream(RandomBytes(64 * 1024, seed: 81));

            await foreach (var _ in strategy.ChunkAsync(source, cts.Token))
            {
                Assert.Fail("A cancelled token must not yield a chunk.");
            }
        });
    }

    [Test]
    public void Chunk_NullSource_ThrowsArgumentNullException()
    {
        var strategy = new FastCdcChunkingStrategy();

        _ = Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in strategy.ChunkAsync(null!))
            {
                Assert.Fail("A null source must not yield a chunk.");
            }
        });
    }

    /// <summary>
    /// A read-only stream that never hands back more than a fixed number of bytes per read, standing
    /// in for a source that satisfies a read only partially.
    /// </summary>
    /// <remarks>
    /// Production reads through <see cref="FileStream"/> and, for network destinations, through
    /// streams that satisfy a read only partially. If chunk boundaries depended on read granularity,
    /// the same file would deduplicate differently on a local run than on a network one and the
    /// backup would silently grow, with nothing reporting an error.
    /// </remarks>
    /// <param name="content">The bytes the stream serves.</param>
    /// <param name="maxBytesPerRead">The most bytes any single read may return.</param>
    private sealed class DripStream(byte[] content, int maxBytesPerRead) : Stream
    {
        /// <summary>
        /// The bytes served by this stream.
        /// </summary>
        private readonly byte[] content = content;

        /// <summary>
        /// The cap applied to every read, which forces the chunker's refill loop to iterate.
        /// </summary>
        private readonly int maxBytesPerRead = maxBytesPerRead;

        /// <summary>
        /// The offset of the next byte to serve.
        /// </summary>
        private int position;

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => this.content.Length;

        /// <inheritdoc/>
        public override long Position
        {
            get => this.position;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(
                Math.Min(buffer.Length, this.maxBytesPerRead),
                this.content.Length - this.position
            );

            if (count <= 0)
            {
                return 0;
            }

            this.content.AsSpan(this.position, count).CopyTo(buffer);
            this.position += count;
            return count;
        }

        /// <inheritdoc/>
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        /// <inheritdoc/>
        public override void Flush()
        {
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
