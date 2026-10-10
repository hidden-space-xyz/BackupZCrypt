namespace BackupZCrypt.Application.Engine;

/// <summary>
/// Classifies the failures an operation meets while it works through the files of a backup.
/// </summary>
internal static class FileFailures
{
    /// <summary>
    /// Determines whether a failure is confined to the file being processed, in which case the run
    /// records the error and moves on instead of aborting the whole operation.
    /// </summary>
    /// <remarks>
    /// <see cref="FileNotFoundException"/>, <see cref="DirectoryNotFoundException"/>, and
    /// <see cref="PathTooLongException"/> all derive from <see cref="IOException"/>. Invalid archive
    /// data is also confined to the manifest entry currently being processed.
    /// </remarks>
    /// <param name="ex">The exception thrown while processing a file.</param>
    /// <returns><see langword="true"/> if the failure affects only one file; otherwise <see langword="false"/>.</returns>
    internal static bool IsConfinedToOneFile(Exception ex)
    {
        return ex is IOException or InvalidDataException or UnauthorizedAccessException;
    }
}
