using BackupZCrypt.Infrastructure.FileSystem;

namespace BackupZCrypt.Test.Unit.Infrastructure.FileSystem;

/// <summary>
/// Unit tests for the attribute arithmetic <see cref="FileMetadataService"/> applies on Windows.
/// </summary>
/// <remarks>
/// Only Windows writes the hidden and read-only attributes back, so the flag computation is checked
/// as a pure function here to keep it verified on every platform the suite runs on.
/// </remarks>
public sealed class FileMetadataServiceTests
{
    [Theory]
    [InlineData(FileAttributes.Archive, true, true, FileAttributes.Archive | FileAttributes.ReadOnly | FileAttributes.Hidden)]
    [InlineData(FileAttributes.Archive | FileAttributes.ReadOnly | FileAttributes.Hidden, false, false, FileAttributes.Archive)]
    [InlineData(FileAttributes.ReadOnly | FileAttributes.Hidden, null, null, FileAttributes.ReadOnly | FileAttributes.Hidden)]
    [InlineData(FileAttributes.Hidden, true, null, FileAttributes.Hidden | FileAttributes.ReadOnly)]
    [InlineData(FileAttributes.ReadOnly, null, false, FileAttributes.ReadOnly)]
    internal void WithRecordedFlags_SetsOrClearsOnlyTheRecordedFlags(
        FileAttributes current,
        bool? isReadOnly,
        bool? isHidden,
        FileAttributes expected
    )
    {
        Assert.Equal(expected, FileMetadataService.WithRecordedFlags(current, isReadOnly, isHidden));
    }
}
