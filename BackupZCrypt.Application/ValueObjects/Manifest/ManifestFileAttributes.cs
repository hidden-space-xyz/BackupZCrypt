namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The portable file attributes a manifest records for an entry, stored as a bit field.
/// </summary>
[Flags]
public enum ManifestFileAttributes
{
    /// <summary>
    /// No recorded attribute.
    /// </summary>
    None = 0,

    /// <summary>
    /// The file was marked read-only.
    /// </summary>
    ReadOnly = 1,

    /// <summary>
    /// The file was marked hidden on Windows.
    /// </summary>
    Hidden = 2,
}
