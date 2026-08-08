using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Requests the detection of the manifest kind at a backup path, so the UI can tell whether a folder
/// already holds an encrypted archive.
/// </summary>
/// <param name="BackupPath">A path to the backup directory or a file within it.</param>
public sealed record class DetectManifestKindQuery(string BackupPath) : IQuery<ManifestKind>;
