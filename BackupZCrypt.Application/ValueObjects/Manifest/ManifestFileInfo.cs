namespace BackupZCrypt.Application.ValueObjects.Manifest;

public sealed record ManifestFileInfo(
    string OriginalRelativePath,
    byte[] Salt,
    byte[] Nonce,
    string SourceHash);
