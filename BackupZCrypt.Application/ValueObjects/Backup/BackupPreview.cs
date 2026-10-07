namespace BackupZCrypt.Application.ValueObjects.Backup;

/// <summary>
/// What an existing backup holds compared with what an operation is about to do with it, gathered
/// before the operation runs so its consequences can be confirmed first.
/// </summary>
/// <param name="FileCount">The number of files recorded in the backup.</param>
/// <param name="TotalBytes">The total original size of the files recorded in the backup.</param>
/// <param name="MatchedFiles">For an update, the number of recorded files still present in the source.</param>
/// <param name="RemovedPaths">For an update, the recorded files the update removes, ordered by path.</param>
/// <param name="BytesToProcess">
/// For an update, the size of the source files that are new or have changed since the backup.
/// </param>
public sealed record class BackupPreview(
    int FileCount,
    long TotalBytes,
    int MatchedFiles,
    IReadOnlyList<string> RemovedPaths,
    long BytesToProcess
);
