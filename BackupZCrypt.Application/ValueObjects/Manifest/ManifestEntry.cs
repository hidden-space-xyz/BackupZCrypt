namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// A single file entry in a plain (non-chunked) backup manifest.
/// </summary>
/// <param name="RelativePath">The path of the stored backup file relative to the backup root.</param>
/// <param name="OriginalRelativePath">The original source path relative to the source root.</param>
/// <param name="Salt">The Base64-encoded salt used to derive this file's key.</param>
/// <param name="Nonce">The Base64-encoded nonce used to encrypt this file.</param>
/// <param name="SourceHash">The Base64-encoded hash of the source file, used to verify restores.</param>
public sealed record ManifestEntry(
    string RelativePath,
    string OriginalRelativePath,
    string Salt,
    string Nonce,
    string SourceHash
);
