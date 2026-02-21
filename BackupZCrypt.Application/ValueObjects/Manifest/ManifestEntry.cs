namespace BackupZCrypt.Application.ValueObjects.Manifest;

public sealed record ManifestEntry(string OriginalRelativePath, string ObfuscatedRelativePath);
