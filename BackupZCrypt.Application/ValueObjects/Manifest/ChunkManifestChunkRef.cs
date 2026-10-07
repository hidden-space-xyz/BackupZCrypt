namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// References a stored chunk within a manifest file entry.
/// </summary>
/// <remarks>
/// The chunk's nonce is not recorded: it is derived from the chunk-nonce sub-key and
/// <paramref name="Hash"/> whenever the chunk is encrypted or decrypted.
/// </remarks>
/// <param name="Hash">
/// The Base64-encoded SHA-256 hash of the chunk's plaintext. It keys deduplication and is the input to the
/// keyed HMACs that produce the chunk's on-disk file name and its nonce; it is never the file name itself.
/// </param>
/// <param name="Size">The plaintext length of the chunk in bytes.</param>
public sealed record class ChunkManifestChunkRef(string Hash, int Size);
