namespace BackupZCrypt.Domain.Enums;

/// <summary>
/// Identifies which kind of backup operation is being requested.
/// </summary>
public enum BackupOperation
{
    /// <summary>
    /// Create a new backup from a source.
    /// </summary>
    Create = 0,

    /// <summary>
    /// Restore files from an existing backup.
    /// </summary>
    Restore = 1,

    /// <summary>
    /// Update an existing backup with changes from the source.
    /// </summary>
    Update = 2,
}
