namespace BackupZCrypt.Application.ValueObjects.Manifest;

public sealed record ManifestEntry(
    string RelativePath,
    string OriginalRelativePath,
    string Salt,
    string Nonce,
    string SourceHash);
