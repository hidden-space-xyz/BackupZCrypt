using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// An existing backup whose manifest has been decrypted, holding the keys that opened it until it
/// is disposed.
/// </summary>
/// <param name="preamble">The manifest preamble read from disk.</param>
/// <param name="keys">The keys derived for the backup, owned by this instance.</param>
/// <param name="manifest">The decrypted manifest.</param>
internal sealed class OpenedBackup(
    ManifestPreamble preamble,
    ArchiveKeys keys,
    ChunkManifestData manifest
) : IDisposable
{
    /// <summary>
    /// Gets the manifest preamble read from disk.
    /// </summary>
    public ManifestPreamble Preamble { get; } = preamble;

    /// <summary>
    /// Gets the keys derived for the backup.
    /// </summary>
    public ArchiveKeys Keys { get; } = keys;

    /// <summary>
    /// Gets the decrypted manifest.
    /// </summary>
    public ChunkManifestData Manifest { get; } = manifest;

    /// <summary>
    /// Wipes the keys.
    /// </summary>
    public void Dispose()
    {
        this.Keys.Dispose();
    }
}
