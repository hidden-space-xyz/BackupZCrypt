using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Test.Unit.Domain.Archive;

/// <summary>
/// Unit tests for <see cref="BackupLayout"/>, which decides which names in a backup folder belong to
/// this program and may therefore be cleaned up.
/// </summary>
/// <remarks>
/// The writer names its temporary files with <see cref="BackupLayout.NewTemporaryFileName"/> and the
/// cleanup recognizes them with <see cref="BackupLayout.IsTemporaryFileName"/>; if the two drifted
/// apart, a crashed run's leftovers would never be pruned and a create would refuse a folder that
/// holds nothing but a backup.
/// </remarks>
public sealed class BackupLayoutTests
{
    /// <summary>
    /// A valid chunk file name: 64 hexadecimal characters and the archive's file extension.
    /// </summary>
    private static readonly string ChunkName = new string('a', 64) + BackupConstants.AppFileExtension;

    [Fact]
    internal void NewTemporaryFileName_IsRecognizedAsTemporaryAndAsNothingElse()
    {
        var name = BackupLayout.NewTemporaryFileName();

        Assert.Multiple(
            () => Assert.True(BackupLayout.IsTemporaryFileName(name)),
            () => Assert.False(BackupLayout.IsChunkFileName(name)),
            () => Assert.False(BackupLayout.IsQuarantinedChunkFileName(name)),
            () => Assert.Equal(name, Path.GetFileName(name))
        );
    }

    [Fact]
    internal void NewTemporaryFileName_IsFreshOnEveryCall()
    {
        Assert.NotEqual(BackupLayout.NewTemporaryFileName(), BackupLayout.NewTemporaryFileName());
    }

    [Theory]
    [InlineData(".0123456789abcdef01234567.tmp", true)]
    [InlineData(".0123456789ABCDEF01234567.TMP", true)]
    [InlineData("0123456789abcdef01234567.tmp", false)]
    [InlineData(".0123456789abcdef0123456.tmp", false)]
    [InlineData(".0123456789abcdef012345678.tmp", false)]
    [InlineData(".0123456789abcdef0123456g.tmp", false)]
    [InlineData(".0123456789abcdef01234567.txt", false)]
    internal void IsTemporaryFileName_AcceptsOnlyTheWrittenShape(string name, bool expected)
    {
        Assert.Equal(expected, BackupLayout.IsTemporaryFileName(name));
    }

    [Fact]
    internal void ChunkNames_AreRecognizedPlainAndQuarantined()
    {
        Assert.Multiple(
            () => Assert.True(BackupLayout.IsChunkFileName(ChunkName)),
            () => Assert.False(BackupLayout.IsQuarantinedChunkFileName(ChunkName)),
            () => Assert.True(BackupLayout.IsQuarantinedChunkFileName(ChunkName + BackupConstants.QuarantineExtension)),
            () => Assert.False(BackupLayout.IsChunkFileName(ChunkName + BackupConstants.QuarantineExtension)),
            () => Assert.False(BackupLayout.IsChunkFileName("g" + ChunkName[1..]))
        );
    }
}
