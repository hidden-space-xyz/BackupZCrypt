namespace BackupZCrypt.Domain.ValueObjects.FileSystem;

/// <summary>
/// A folder whose contents could not be listed while a directory tree was walked.
/// </summary>
/// <param name="Path">The full path of the folder.</param>
/// <param name="Error">The error raised when the folder was opened.</param>
public sealed record class InaccessibleDirectory(string Path, Exception Error);
