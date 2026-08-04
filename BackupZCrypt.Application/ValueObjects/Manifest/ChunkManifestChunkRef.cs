namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// References a stored chunk within a manifest file entry.
/// </summary>
/// <param name="Hash">
/// The Base64-encoded SHA-256 hash of the chunk's plaintext. It keys deduplication and is the input to the
/// keyed HMAC that produces the chunk's on-disk file name; it is never the file name itself.
/// </param>
/// <param name="Size">The plaintext length of the chunk in bytes.</param>
/// <param name="Nonce">The Base64-encoded nonce used to encrypt the chunk.</param>
public sealed record class ChunkManifestChunkRef(string Hash, int Size, string Nonce);
