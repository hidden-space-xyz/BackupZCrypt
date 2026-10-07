namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// Describes what was found at the location a user picked as a backup.
/// </summary>
public enum ManifestKind
{
    /// <summary>
    /// The folder exists but holds no manifest.
    /// </summary>
    Missing = 0,

    /// <summary>
    /// A chunked manifest whose document is AEAD-encrypted behind an unencrypted preamble, so a
    /// password is required to read it.
    /// </summary>
    Encrypted = 1,

    /// <summary>
    /// A manifest exists but is empty or truncated.
    /// </summary>
    Damaged = 2,

    /// <summary>
    /// A manifest exists but names an algorithm this version cannot open.
    /// </summary>
    Unsupported = 3,

    /// <summary>
    /// Nothing exists at the path.
    /// </summary>
    PathNotFound = 4,

    /// <summary>
    /// The path names a file rather than the backup folder.
    /// </summary>
    NotADirectory = 5,
}
