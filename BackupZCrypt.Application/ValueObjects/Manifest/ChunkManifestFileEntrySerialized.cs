using System.Text.Json.Serialization;

namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The JSON-serializable on-disk shape of a single backed-up file entry.
/// </summary>
/// <remarks>
/// The metadata members are optional and omitted when absent, so manifests written before they
/// existed still deserialize, and readers that predate them skip them as unknown members.
/// </remarks>
/// <param name="OriginalPath">The file's path relative to the backup root.</param>
/// <param name="FileHash">The Base64-encoded SHA-256 hash of the whole file, used to verify restores.</param>
/// <param name="TotalSize">The original file size in bytes.</param>
/// <param name="Chunks">The ordered chunk references that reconstruct the file.</param>
/// <param name="LastWriteTimeUtc">The file's modification time in UTC, when recorded.</param>
/// <param name="Attributes">The portable attributes recorded for the file, when any were.</param>
/// <param name="UnixMode">The Unix permission bits, when the file came from a Unix system.</param>
internal sealed record class ChunkManifestFileEntrySerialized(
    string OriginalPath,
    string FileHash,
    long TotalSize,
    List<ChunkManifestChunkRef> Chunks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? LastWriteTimeUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManifestFileAttributes? Attributes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UnixMode = null
);
