using System.Text.Json.Serialization;

namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The JSON-serializable on-disk shape of a single backed-up file entry.
/// </summary>
/// <remarks>
/// A file stored as a single chunk, as every file of up to 256 KiB is, omits
/// <paramref name="Chunks"/>: that chunk's hash is the file hash and its size is the file size, so the
/// entry already records both. An empty file, which has no chunks, omits it too.
/// </remarks>
/// <param name="OriginalPath">The file's path relative to the backup root.</param>
/// <param name="FileHash">The Base64-encoded SHA-256 hash of the whole file, used to verify restores.</param>
/// <param name="TotalSize">The original file size in bytes.</param>
/// <param name="LastWriteTimeUtc">The file's modification time in UTC.</param>
/// <param name="Chunks">
/// The ordered chunk references that reconstruct the file, or <see langword="null"/> when the file is
/// empty or is its own single chunk.
/// </param>
/// <param name="Attributes">The portable attributes recorded for the file, when any were.</param>
/// <param name="UnixMode">The Unix permission bits, when the file came from a Unix system.</param>
internal sealed record class ChunkManifestFileEntrySerialized(
    string OriginalPath,
    string FileHash,
    long TotalSize,
    DateTime LastWriteTimeUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<ChunkManifestChunkRef>? Chunks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ManifestFileAttributes? Attributes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UnixMode = null
);
