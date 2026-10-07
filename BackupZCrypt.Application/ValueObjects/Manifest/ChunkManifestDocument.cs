using System.Text.Json.Serialization;

using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The JSON-serializable on-disk shape of a chunked backup manifest before encryption.
/// </summary>
/// <remarks>
/// The encryption algorithm, the key derivation function, and the master salt are recorded only in
/// the unencrypted preamble, which is bound into the ciphertext as associated data, so the document
/// holds nothing the preamble already does.
/// </remarks>
/// <param name="Compression">The compression mode applied to chunks.</param>
/// <param name="Files">The serialized file entries contained in the backup.</param>
/// <param name="Directories">
/// The relative paths of the empty folders a restore recreates, omitted when there are none.
/// </param>
internal sealed record class ChunkManifestDocument(
    CompressionMode Compression,
    List<ChunkManifestFileEntrySerialized> Files,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<string>? Directories = null
);
