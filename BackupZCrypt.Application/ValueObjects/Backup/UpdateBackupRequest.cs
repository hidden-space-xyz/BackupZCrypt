namespace BackupZCrypt.Application.ValueObjects.Backup;

/// <summary>
/// Describes an update of an existing archive from the current state of its source folder. The
/// algorithms are read from the archive, which is why this request carries none.
/// </summary>
/// <param name="SourcePath">The folder whose current contents feed the update.</param>
/// <param name="DestinationPath">The folder holding the archive to update.</param>
/// <param name="Password">The password the archive was created with.</param>
/// <param name="ProceedOnWarnings">Whether to continue when validation produces non-fatal warnings.</param>
public sealed record class UpdateBackupRequest(
    string SourcePath,
    string DestinationPath,
    string Password,
    bool ProceedOnWarnings = false
) : BackupRequest(SourcePath, DestinationPath, Password, ProceedOnWarnings);
