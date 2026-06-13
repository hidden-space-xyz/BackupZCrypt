using BackupZCrypt.Infrastructure.Strategies.Chunking;

namespace BackupZCrypt.Test.Unit.Infrastructure;

public sealed class FastCdcChunkingTests
{
    private const int ChunkMaxSize = 4 * 1024 * 1024;

    private static byte[] RandomBytes(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

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

    private static byte[] Concat(IReadOnlyList<byte[]> chunks)
    {
        using MemoryStream stream = new();
        foreach (var chunk in chunks)
        {
            stream.Write(chunk);
        }

        return stream.ToArray();
    }

    [Fact]
    public async Task EmptyInput_YieldsNoChunks()
    {
        var chunks = await DrainAsync([]);

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task SmallInput_YieldsSingleChunkEqualToInput()
    {
        // ~10 KiB is below the 256 KiB minimum, so it cannot be split.
        var input = RandomBytes(10 * 1024, seed: 1);

        var chunks = await DrainAsync(input);

        Assert.Single(chunks);
        Assert.Equal(input, chunks[0]);
    }

    [Fact]
    public async Task LargeInput_ReassemblesToOriginal_WithMultipleChunks()
    {
        // ~10 MiB exceeds the 4 MiB max, so it must split into several chunks.
        var input = RandomBytes(10 * 1024 * 1024, seed: 2);

        var chunks = await DrainAsync(input);

        Assert.True(chunks.Count > 1, $"Expected multiple chunks, got {chunks.Count}.");
        Assert.Equal(input, Concat(chunks));
    }

    [Fact]
    public async Task EveryChunk_DoesNotExceedMaxSize()
    {
        var input = RandomBytes(10 * 1024 * 1024, seed: 3);

        var chunks = await DrainAsync(input);

        Assert.All(chunks, chunk => Assert.True(chunk.Length <= ChunkMaxSize));
    }

    [Fact]
    public async Task Chunking_IsDeterministic_AcrossRuns()
    {
        var input = RandomBytes(10 * 1024 * 1024, seed: 4);

        var first = await DrainAsync(input);
        var second = await DrainAsync(input);

        var firstLengths = first.Select(c => c.Length).ToArray();
        var secondLengths = second.Select(c => c.Length).ToArray();

        Assert.Equal(firstLengths, secondLengths);
    }

    [Fact]
    public async Task ChunkAsync_NullSource_ThrowsArgumentNullException()
    {
        var strategy = new FastCdcChunkingStrategy();

        // The guard lives in an async iterator, so it only fires once enumeration starts.
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await foreach (var _ in strategy.ChunkAsync(null!))
            {
                // Unreachable: the first MoveNextAsync throws.
            }
        });
    }
}
