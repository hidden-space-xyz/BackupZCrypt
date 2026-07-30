namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// Identifies the on-disk format of the backup manifest found at a backup location.
/// </summary>
public enum ManifestKind
{
    /// <summary>
    /// No manifest file was found, or it was empty or unreadable.
    /// </summary>
    Missing = 0,

    /// <summary>
    /// A chunked manifest whose document is AEAD-encrypted behind an unencrypted preamble, so a
    /// password is required to read it. Every non-empty manifest is reported as this kind, since
    /// encryption is the only supported format.
    /// </summary>
    Encrypted = 1,
}
