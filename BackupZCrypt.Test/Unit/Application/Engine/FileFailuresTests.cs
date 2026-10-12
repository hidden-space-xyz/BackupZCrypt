using System.Security.Cryptography;

using BackupZCrypt.Application.Engine;

namespace BackupZCrypt.Test.Unit.Application.Engine;

/// <summary>
/// Unit tests for <see cref="FileFailures"/>, which decides whether a failure only skips the file
/// being processed or aborts the whole operation.
/// </summary>
public sealed class FileFailuresTests
{
    /// <summary>
    /// Supplies failures that belong to a single file and must never abort a run.
    /// </summary>
    /// <returns>One exception per confined failure family.</returns>
    public static TheoryData<Exception> ConfinedFailures()
    {
        return new()
        {
            new IOException("read failed"),
            new FileNotFoundException("vanished"),
            new DirectoryNotFoundException("vanished"),
            new PathTooLongException("too long"),
            new MissingChunkException(),
            new InvalidDataException("bad entry"),
            new UnauthorizedAccessException("denied"),
        };
    }

    /// <summary>
    /// Supplies failures that say nothing about one file and therefore abort the run.
    /// </summary>
    /// <returns>One exception per failure that is not confined to a file.</returns>
    public static TheoryData<Exception> UnconfinedFailures()
    {
        return new()
        {
            new InvalidOperationException("engine bug"),
            new CryptographicException("not a file failure on its own"),
            new OperationCanceledException(),
            new ArgumentException("bad argument"),
        };
    }

    [Theory]
    [MemberData(nameof(ConfinedFailures))]
    internal void IsConfinedToOneFile_FileLevelFailure_ReturnsTrue(Exception exception)
    {
        Assert.True(FileFailures.IsConfinedToOneFile(exception));
    }

    [Theory]
    [MemberData(nameof(UnconfinedFailures))]
    internal void IsConfinedToOneFile_OperationLevelFailure_ReturnsFalse(Exception exception)
    {
        Assert.False(FileFailures.IsConfinedToOneFile(exception));
    }
}
