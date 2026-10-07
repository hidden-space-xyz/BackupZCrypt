namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The decrypted contents of a chunked backup manifest.
/// </summary>
/// <param name="Header">
/// The algorithm and compression metadata describing the backup. The algorithms are read from the
/// manifest preamble and the compression mode from the encrypted document.
/// </param>
/// <param name="MasterSalt">The Base64-encoded master salt used to derive the backup keys, read from the preamble.</param>
/// <param name="Files">The set of backed-up files and their chunk references.</param>
/// <param name="Directories">
/// The relative paths of the empty folders a restore recreates, or <see langword="null"/> when there
/// are none.
/// </param>
public sealed record class ChunkManifestData(
    ManifestHeader Header,
    string MasterSalt,
    IReadOnlyList<ChunkManifestFileEntry> Files,
    IReadOnlyList<string>? Directories = null
);
