using BackupZCrypt.Infrastructure.Strategies.Chunking;

namespace BackupZCrypt.Test.Unit.Infrastructure;

/// <summary>
/// Unit tests for the FastCDC content-defined chunking strategy.
/// </summary>
public sealed class FastCdcChunkingTests
{
    /// <summary>
    /// The 4 MiB upper bound the strategy enforces on any chunk, mirrored here because the
    /// production constant is private.
    /// </summary>
    private const int ChunkMaxSize = 4 * 1024 * 1024;

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
    /// Chunks the input and materializes each yielded memory block as an array so tests can compare
    /// contents directly.
    /// </summary>
    /// <param name="input">The bytes to feed through the chunker.</param>
    /// <returns>The chunks in the order they were produced.</returns>
    private static async Task<List<byte[]>> DrainAsync(byte[] input)
    {
        var strategy = new FastCdcChunkingStrategy();
        List<byte[]> chunks = [];

        await foreach (var chunk in strategy.ChunkAsync(new MemoryStream(input)))
        {
            chunks.Add(chunk.ToArray());
        }

        return chunks;
    }

    /// <summary>
    /// Joins chunks back together so a test can assert the split is lossless.
    /// </summary>
    /// <param name="chunks">The chunks to concatenate, in order.</param>
    /// <returns>The reassembled byte sequence.</returns>
    private static byte[] Concat(IReadOnlyList<byte[]> chunks)
    {
        using MemoryStream stream = new();
        foreach (var chunk in chunks)
        {
            stream.Write(chunk);
        }

        return stream.ToArray();
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
            Assert.That(Concat(chunks), Is.EqualTo(input));
        }
    }

    [Test]
    public async Task EveryChunk_DoesNotExceedMaxSize()
    {
        var input = RandomBytes(10 * 1024 * 1024, seed: 3);

        var chunks = await DrainAsync(input);

        Assert.That(chunks, Has.All.Matches<byte[]>(chunk => chunk.Length <= ChunkMaxSize));
    }

    [Test]
    public async Task Chunking_IsDeterministic_AcrossRuns()
    {
        var input = RandomBytes(10 * 1024 * 1024, seed: 4);

        var first = await DrainAsync(input);
        var second = await DrainAsync(input);

        var firstLengths = first.Select(c => c.Length).ToArray();
        var secondLengths = second.Select(c => c.Length).ToArray();

        Assert.That(secondLengths, Is.EqualTo(firstLengths));
    }

    [Test]
    public void ChunkAsync_NullSource_ThrowsArgumentNullException()
    {
        var strategy = new FastCdcChunkingStrategy();

        _ = Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in strategy.ChunkAsync(null!))
            {
            }
        });
    }
}
