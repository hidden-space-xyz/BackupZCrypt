namespace BackupZCrypt.Application.ValueObjects.Manifest;

public enum ManifestKind
{
    Missing = 0,
    PlainCopy = 1,
    UnencryptedChunked = 2,
    Encrypted = 3,
}
