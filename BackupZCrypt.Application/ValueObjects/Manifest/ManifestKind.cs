namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// Identifies the on-disk format of a backup manifest, inferred from its first byte.
/// </summary>
public enum ManifestKind
{
    /// <summary>
    /// No manifest file was found or it was empty.
    /// </summary>
    Missing = 0,

    /// <summary>
    /// A plain JSON manifest written by an unencrypted, non-chunked plain copy.
    /// </summary>
    PlainCopy = 1,

    /// <summary>
    /// A chunked manifest created without encryption (<see cref="BackupZCrypt.Domain.Enums.EncryptionAlgorithm.None"/>).
    /// </summary>
    UnencryptedChunked = 2,

    /// <summary>
    /// A chunked manifest protected by an encrypted preamble.
    /// </summary>
    Encrypted = 3,
}
