namespace BackupZCrypt.Application.ValueObjects.Backup;

/// <summary>
/// Describes a restore of an existing archive into a destination folder. The algorithms are read
/// from the archive, which is why this request carries none.
/// </summary>
/// <param name="SourcePath">The folder holding the archive to restore.</param>
/// <param name="DestinationPath">The folder the files are rebuilt into.</param>
/// <param name="Password">The password the archive was created with.</param>
/// <param name="ProceedOnWarnings">Whether to continue when validation produces non-fatal warnings.</param>
public sealed record class RestoreBackupRequest(
    string SourcePath,
    string DestinationPath,
    string Password,
    bool ProceedOnWarnings = false
) : BackupRequest(SourcePath, DestinationPath, Password, ProceedOnWarnings);
