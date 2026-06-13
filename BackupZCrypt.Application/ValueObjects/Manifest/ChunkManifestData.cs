namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The decrypted contents of a chunked backup manifest.
/// </summary>
/// <param name="Header">The algorithm and compression metadata describing the backup.</param>
/// <param name="MasterSalt">The Base64-encoded master salt used to derive the backup keys.</param>
/// <param name="Files">The set of backed-up files and their chunk references.</param>
public sealed record ChunkManifestData(
    ManifestHeader Header,
    string MasterSalt,
    IReadOnlyList<ChunkManifestFileEntry> Files
);
