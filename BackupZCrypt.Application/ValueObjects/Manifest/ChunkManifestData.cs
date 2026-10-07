namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The decrypted contents of a chunked backup manifest.
/// </summary>
/// <param name="Header">The algorithm and compression metadata describing the backup.</param>
/// <param name="MasterSalt">The Base64-encoded master salt used to derive the backup keys.</param>
/// <param name="Files">The set of backed-up files and their chunk references.</param>
/// <param name="Directories">
/// The relative paths of the empty folders a restore recreates, or <see langword="null"/> for
/// manifests written before they were recorded.
/// </param>
public sealed record class ChunkManifestData(
    ManifestHeader Header,
    string MasterSalt,
    IReadOnlyList<ChunkManifestFileEntry> Files,
    IReadOnlyList<string>? Directories = null
);
