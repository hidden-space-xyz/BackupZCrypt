using System.Security.Cryptography;
using System.Text;

using BackupZCrypt.Application.Engine;
using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Test.Unit.Application.Engine;

/// <summary>
/// Unit tests for <see cref="ManifestEntries"/>, the up-front sweep that rejects a malformed or
/// crafted manifest before any file is touched, and the projections that relate an entry to the file
/// it describes.
/// </summary>
public sealed class ManifestEntriesTests
{
    /// <summary>
    /// The modification time every entry and metadata sample in this fixture carries.
    /// </summary>
    private static readonly DateTime RecordedAt = new(2026, 3, 14, 15, 9, 26, DateTimeKind.Utc);

    [Fact]
    internal void Validate_WellFormedEntriesSharingAChunk_IsAccepted()
    {
        var shared = Chunk("shared", 10);

        var exception = Record.Exception(
            () => ManifestEntries.Validate([Entry("a.txt", shared), Entry("dir/b.txt", shared, Chunk("own", 5))])
        );

        Assert.Null(exception);
    }

    [Fact]
    internal void Validate_DuplicatePath_IsRejected()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => ManifestEntries.Validate([Entry("a.txt", Chunk("one", 3)), Entry("a.txt", Chunk("two", 3))])
        );
    }

    [Fact]
    internal void Validate_NegativeFileSize_IsRejected()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => ManifestEntries.Validate([Entry("a.txt") with { TotalSize = -1 }])
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(BackupConstants.MaximumChunkSize + 1)]
    internal void Validate_ChunkSizeOutsideTheSupportedRange_IsRejected(int size)
    {
        _ = Assert.Throws<InvalidDataException>(() => ManifestEntries.Validate([Entry("a.txt", Chunk("c", size))]));
    }

    [Fact]
    internal void Validate_OneChunkHashRecordedWithTwoSizes_IsRejected()
    {
        var hash = Hash("shared");

        _ = Assert.Throws<InvalidDataException>(
            () =>
                ManifestEntries.Validate(
                    [Entry("a.txt", new ChunkManifestChunkRef(hash, 10)), Entry("b.txt", new ChunkManifestChunkRef(hash, 11))]
                )
        );
    }

    [Fact]
    internal void Validate_ChunkSizesThatDoNotAddUpToTheFileSize_IsRejected()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => ManifestEntries.Validate([Entry("a.txt", Chunk("c", 10)) with { TotalSize = 11 }])
        );
    }

    [Fact]
    internal void Validate_InvalidPath_IsRejected()
    {
        _ = Assert.Throws<InvalidDataException>(() => ManifestEntries.Validate([Entry("../escape.txt")]));
    }

    [Theory]
    [InlineData("not base64 at all!")]
    [InlineData("AAAA")]
    internal void Validate_FileHashThatIsNotASha256Digest_IsRejected(string fileHash)
    {
        _ = Assert.Throws<CryptographicException>(
            () => ManifestEntries.Validate([Entry("a.txt") with { FileHash = fileHash }])
        );
    }

    [Fact]
    internal void Validate_FileHashInANonCanonicalBase64Spelling_IsRejected()
    {
        var spaced = Hash("content").Insert(4, " ");

        _ = Assert.Throws<CryptographicException>(
            () => ManifestEntries.Validate([Entry("a.txt") with { FileHash = spaced }])
        );
    }

    [Fact]
    internal void Validate_ChunkHashInANonCanonicalBase64Spelling_IsRejected()
    {
        var spaced = Hash("chunk").Insert(8, "\n");

        _ = Assert.Throws<CryptographicException>(
            () => ManifestEntries.Validate([Entry("a.txt", new ChunkManifestChunkRef(spaced, 4))])
        );
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, ManifestFileAttributes.ReadOnly)]
    [InlineData(false, true, ManifestFileAttributes.Hidden)]
    [InlineData(true, true, ManifestFileAttributes.ReadOnly | ManifestFileAttributes.Hidden)]
    internal void WithMetadata_RecordsOnlyTheAttributesTheFileCarries(
        bool isReadOnly,
        bool isHidden,
        ManifestFileAttributes? expected
    )
    {
        var local = DateTime.SpecifyKind(RecordedAt, DateTimeKind.Unspecified);

        var entry = ManifestEntries.WithMetadata(
            Entry("a.txt"),
            new FileMetadata(0, local, isReadOnly, isHidden, 0b110_100_100)
        );

        Assert.Multiple(
            () => Assert.Equal(expected, entry.Attributes),
            () => Assert.Equal(DateTimeKind.Utc, entry.LastWriteTimeUtc.Kind),
            () => Assert.Equal(RecordedAt.Ticks, entry.LastWriteTimeUtc.Ticks),
            () => Assert.Equal(0b110_100_100, entry.UnixMode)
        );
    }

    [Theory]
    [InlineData(10L, 0L, true)]
    [InlineData(11L, 0L, false)]
    [InlineData(10L, 1L, false)]
    internal void IsUnchanged_ComparesSizeAndModificationTime(long size, long tickOffset, bool expected)
    {
        var recorded = Entry("a.txt", Chunk("c", 10));
        FileMetadata metadata = new(size, RecordedAt.AddTicks(tickOffset), false, false, null);

        Assert.Equal(expected, ManifestEntries.IsUnchanged(recorded, metadata));
    }

    [Theory]
    [InlineData("%%%")]
    [InlineData("AAAA")]
    internal void DecodeBase64FixedLength_ValueOfTheWrongShape_ThrowsWithTheGivenMessage(string value)
    {
        var exception = Assert.Throws<CryptographicException>(
            () => ManifestEntries.DecodeBase64FixedLength(value, SHA256.HashSizeInBytes, "Invalid test hash.")
        );

        Assert.Equal("Invalid test hash.", exception.Message);
    }

    /// <summary>
    /// Computes the canonical Base64 SHA-256 digest of a label, so each sample has a distinct, valid hash.
    /// </summary>
    /// <param name="label">The text whose digest is taken.</param>
    /// <returns>The canonical Base64 digest.</returns>
    private static string Hash(string label)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(label)));
    }

    /// <summary>
    /// Builds a chunk reference whose hash is derived from a label.
    /// </summary>
    /// <param name="label">The text whose digest becomes the chunk hash.</param>
    /// <param name="size">The recorded plaintext size of the chunk.</param>
    /// <returns>The chunk reference.</returns>
    private static ChunkManifestChunkRef Chunk(string label, int size)
    {
        return new ChunkManifestChunkRef(Hash(label), size);
    }

    /// <summary>
    /// Builds a file entry whose declared size is the sum of its chunk sizes.
    /// </summary>
    /// <param name="path">The manifest path of the file.</param>
    /// <param name="chunks">The chunks the file is made of.</param>
    /// <returns>A well-formed entry for that path.</returns>
    private static ChunkManifestFileEntry Entry(string path, params ChunkManifestChunkRef[] chunks)
    {
        return new ChunkManifestFileEntry(
            path,
            Hash(path),
            chunks.Sum(static c => (long)c.Size),
            chunks,
            RecordedAt
        );
    }
}
